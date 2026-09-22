using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GraphCalculator
{
    internal sealed record HlslCompileResult(bool Success, byte[] Bytecode, string Messages);

    internal static class HlslRuntimeCompiler
    {
        private const uint D3DCompileEnableStrictness = 1u << 11;
        private const uint D3DCompileOptimizationLevel3 = 1u << 15;

        [DllImport("d3dcompiler_47.dll", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int D3DCompile(
            IntPtr sourceData,
            nuint sourceSize,
            [MarshalAs(UnmanagedType.LPStr)] string sourceName,
            IntPtr defines,
            IntPtr include,
            [MarshalAs(UnmanagedType.LPStr)] string entryPoint,
            [MarshalAs(UnmanagedType.LPStr)] string target,
            uint flags1,
            uint flags2,
            out IntPtr code,
            out IntPtr errors);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetBufferPointerDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate nuint GetBufferSizeDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseDelegate(IntPtr self);

        public static HlslCompileResult CompilePixelShader(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return new HlslCompileResult(false, Array.Empty<byte>(), "The shader editor is empty.");

            byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
            GCHandle pin = default;
            IntPtr code = IntPtr.Zero;
            IntPtr errors = IntPtr.Zero;

            try
            {
                pin = GCHandle.Alloc(sourceBytes, GCHandleType.Pinned);
                int hr = D3DCompile(
                    pin.AddrOfPinnedObject(),
                    (nuint)sourceBytes.Length,
                    "GraphCalculatorLivePreview.hlsl",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    "main",
                    "ps_3_0",
                    D3DCompileEnableStrictness | D3DCompileOptimizationLevel3,
                    0,
                    out code,
                    out errors);

                string messages = errors != IntPtr.Zero ? ReadBlobText(errors) : string.Empty;
                if (hr < 0 || code == IntPtr.Zero)
                {
                    string text = string.IsNullOrWhiteSpace(messages)
                        ? $"HLSL compiler failed with HRESULT 0x{hr:X8}."
                        : messages.Trim();
                    return new HlslCompileResult(false, Array.Empty<byte>(), text);
                }

                byte[] bytecode = ReadBlobBytes(code);
                return new HlslCompileResult(true, bytecode, messages.Trim());
            }
            catch (DllNotFoundException)
            {
                return new HlslCompileResult(false, Array.Empty<byte>(),
                    "d3dcompiler_47.dll is not available on this Windows installation. The live preview needs the Direct3D shader compiler.");
            }
            catch (EntryPointNotFoundException)
            {
                return new HlslCompileResult(false, Array.Empty<byte>(),
                    "D3DCompile was not found in d3dcompiler_47.dll.");
            }
            catch (Exception ex)
            {
                return new HlslCompileResult(false, Array.Empty<byte>(), "Shader compile failed: " + ex.Message);
            }
            finally
            {
                if (pin.IsAllocated) pin.Free();
                if (code != IntPtr.Zero) ReleaseBlob(code);
                if (errors != IntPtr.Zero) ReleaseBlob(errors);
            }
        }

        private static byte[] ReadBlobBytes(IntPtr blob)
        {
            IntPtr vtable = Marshal.ReadIntPtr(blob);
            IntPtr getPointerAddress = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
            IntPtr getSizeAddress = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
            var getPointer = Marshal.GetDelegateForFunctionPointer<GetBufferPointerDelegate>(getPointerAddress);
            var getSize = Marshal.GetDelegateForFunctionPointer<GetBufferSizeDelegate>(getSizeAddress);

            IntPtr pointer = getPointer(blob);
            int length = checked((int)getSize(blob));
            var data = new byte[length];
            if (length > 0) Marshal.Copy(pointer, data, 0, length);
            return data;
        }

        private static string ReadBlobText(IntPtr blob)
        {
            byte[] data = ReadBlobBytes(blob);
            return Encoding.UTF8.GetString(data).TrimEnd('\0', '\r', '\n');
        }

        private static void ReleaseBlob(IntPtr blob)
        {
            IntPtr vtable = Marshal.ReadIntPtr(blob);
            IntPtr releaseAddress = Marshal.ReadIntPtr(vtable, 2 * IntPtr.Size);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releaseAddress);
            release(blob);
        }
    }

    internal static partial class HlslLivePreviewSource
    {
        private static readonly Regex MainRegex = new(
            @"\b(?:float4|half4)\s+main\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex GraphFunctionRegex = new(
            @"\b(float|float2|float3|float4)\s+GraphFunction\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryBuild(
            string editorSource,
            IReadOnlyDictionary<string, double> parameterValues,
            out string compileSource,
            out string note)
        {
            compileSource = string.Empty;
            note = string.Empty;

            if (string.IsNullOrWhiteSpace(editorSource))
            {
                note = "The shader editor is empty.";
                return false;
            }

            bool hasMain = MainRegex.IsMatch(editorSource);
            Match graphFunction = GraphFunctionRegex.Match(editorSource);
            if (!hasMain && !graphFunction.Success)
            {
                note = "Live preview needs either GraphFunction(...) or a float4 main(float2 uv : TEXCOORD0) : COLOR0 entry point.";
                return false;
            }

            var warnings = new List<string>();
            string previewSource = editorSource;
            if (previewSource.Contains("// Graph Calculator HLSL export", StringComparison.Ordinal)
                && (previewSource.Contains("gc_mandelbrot", StringComparison.Ordinal) || previewSource.Contains("gc_julia", StringComparison.Ordinal)))
            {
                string capped = previewSource.Replace("1,10000)", "1,192)", StringComparison.Ordinal)
                                             .Replace("i<10000", "i<192", StringComparison.Ordinal);
                if (!ReferenceEquals(capped, previewSource) && capped != previewSource)
                {
                    previewSource = capped;
                    warnings.Add("Generated fractal loops are capped at 192 iterations in the ps_3_0 live preview; saved HLSL keeps the editor text unchanged.");
                }
            }

            var source = new StringBuilder(previewSource.Length + 2200);
            source.AppendLine(RuntimePreamble);
            source.AppendLine("#line 1 \"editor\"");
            source.AppendLine(previewSource);

            if (!hasMain)
            {
                string returnType = graphFunction.Groups[1].Value.ToLowerInvariant();
                string arguments = BuildCallArguments(graphFunction.Groups[2].Value, parameterValues, warnings);
                source.AppendLine();
                source.AppendLine("#line 1 \"preview_wrapper\"");
                source.AppendLine("float4 main(float2 uv : TEXCOORD0) : COLOR0");
                source.AppendLine("{");
                source.AppendLine("    float2 gc_p = gc_mapUv(uv);");

                switch (returnType)
                {
                    case "float":
                        source.Append("    float gc_value = GraphFunction(").Append(arguments).AppendLine(");");
                        source.AppendLine("    gc_value = saturate(gc_value);");
                        source.AppendLine("    return float4(gc_value, gc_value, gc_value, 1.0);");
                        break;
                    case "float2":
                        source.Append("    float2 gc_value = GraphFunction(").Append(arguments).AppendLine(");");
                        source.AppendLine("    return float4(gc_colourComplex(gc_value), 1.0);");
                        break;
                    case "float3":
                        source.Append("    float3 gc_value = GraphFunction(").Append(arguments).AppendLine(");");
                        source.AppendLine("    return float4(saturate(gc_value), 1.0);");
                        break;
                    default:
                        source.Append("    float4 gc_value = GraphFunction(").Append(arguments).AppendLine(");");
                        source.AppendLine("    return saturate(gc_value);");
                        break;
                }

                source.AppendLine("}");
            }

            compileSource = source.ToString();
            note = warnings.Count == 0
                ? (hasMain ? "Full pixel-shader entry point" : "GraphFunction wrapped for WPF pixel-shader preview")
                : string.Join(" ", warnings);
            return true;
        }

        private static string BuildCallArguments(
            string parameterList,
            IReadOnlyDictionary<string, double> values,
            List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(parameterList)) return string.Empty;

            var result = new List<string>();
            foreach (string raw in parameterList.Split(','))
            {
                string parameter = raw.Trim();
                if (parameter.Length == 0) continue;

                Match nameMatch = Regex.Match(parameter, @"([A-Za-z_]\w*)\s*(?::[^,]+)?$", RegexOptions.CultureInvariant);
                if (!nameMatch.Success)
                {
                    result.Add("0.0");
                    warnings.Add("One GraphFunction parameter could not be identified and was previewed as 0.");
                    continue;
                }

                string name = nameMatch.Groups[1].Value;
                if (name.Equals("x", StringComparison.OrdinalIgnoreCase)) result.Add("gc_p.x");
                else if (name.Equals("y", StringComparison.OrdinalIgnoreCase)) result.Add("gc_p.y");
                else if (name.Equals("time", StringComparison.OrdinalIgnoreCase) || name.Equals("t", StringComparison.OrdinalIgnoreCase)) result.Add("gc_time");
                else if (name.Equals("z", StringComparison.OrdinalIgnoreCase)) result.Add("0.0");
                else if (TryGetValue(values, name, out double value)) result.Add(value.ToString("R", CultureInfo.InvariantCulture));
                else
                {
                    result.Add("0.0");
                    warnings.Add($"Preview parameter '{name}' has no current calculator value, so it is 0.");
                }
            }

            return string.Join(", ", result);
        }

        private static bool TryGetValue(IReadOnlyDictionary<string, double> values, string name, out double value)
        {
            foreach (KeyValuePair<string, double> pair in values)
            {
                if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private const string RuntimePreamble = @"// Live preview uniforms reserved by Graph Calculator.
float gc_time   : register(c216);
float gc_minX   : register(c217);
float gc_maxX   : register(c218);
float gc_minY   : register(c219);
float gc_maxY   : register(c220);
float gc_width  : register(c221);
float gc_height : register(c222);

float2 gc_mapUv(float2 uv)
{
    return float2(lerp(gc_minX, gc_maxX, uv.x), lerp(gc_maxY, gc_minY, uv.y));
}

float2 gc_resolution()
{
    return float2(gc_width, gc_height);
}

float3 gc_hsvToRgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(0.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1.0,1.0,1.0), saturate(p - 1.0), c.y);
}

float3 gc_colourComplex(float2 z)
{
    float phase = atan2(z.y, z.x);
    float hue = frac(phase / 6.28318530718 + 1.0);
    float magnitude = length(z);
    float value = 1.0 - exp(-0.35 * magnitude);
    return gc_hsvToRgb(float3(hue, 0.85, value));
}
";
    }

    internal sealed class RuntimeHlslEffect : ShaderEffect
    {
        public RuntimeHlslEffect(byte[] bytecode)
        {
            var shader = new PixelShader { ShaderRenderMode = ShaderRenderMode.Auto };
            using var stream = new MemoryStream(bytecode, writable: false);
            shader.SetStreamSource(stream);
            PixelShader = shader;

            UpdateShaderValue(InputProperty);
            UpdateShaderValue(TimeProperty);
            UpdateShaderValue(MinXProperty);
            UpdateShaderValue(MaxXProperty);
            UpdateShaderValue(MinYProperty);
            UpdateShaderValue(MaxYProperty);
            UpdateShaderValue(WidthProperty);
            UpdateShaderValue(HeightProperty);
        }

        public Brush Input
        {
            get => (Brush)GetValue(InputProperty);
            set => SetValue(InputProperty, value);
        }

        public static readonly DependencyProperty InputProperty =
            RegisterPixelShaderSamplerProperty(nameof(Input), typeof(RuntimeHlslEffect), 0);

        public double Time
        {
            get => (double)GetValue(TimeProperty);
            set => SetValue(TimeProperty, value);
        }

        public static readonly DependencyProperty TimeProperty = RegisterConstant(nameof(Time), 0.0, 216);

        public double MinX
        {
            get => (double)GetValue(MinXProperty);
            set => SetValue(MinXProperty, value);
        }

        public static readonly DependencyProperty MinXProperty = RegisterConstant(nameof(MinX), -2.0, 217);

        public double MaxX
        {
            get => (double)GetValue(MaxXProperty);
            set => SetValue(MaxXProperty, value);
        }

        public static readonly DependencyProperty MaxXProperty = RegisterConstant(nameof(MaxX), 2.0, 218);

        public double MinY
        {
            get => (double)GetValue(MinYProperty);
            set => SetValue(MinYProperty, value);
        }

        public static readonly DependencyProperty MinYProperty = RegisterConstant(nameof(MinY), -2.0, 219);

        public double MaxY
        {
            get => (double)GetValue(MaxYProperty);
            set => SetValue(MaxYProperty, value);
        }

        public static readonly DependencyProperty MaxYProperty = RegisterConstant(nameof(MaxY), 2.0, 220);

        public double Width
        {
            get => (double)GetValue(WidthProperty);
            set => SetValue(WidthProperty, value);
        }

        public static readonly DependencyProperty WidthProperty = RegisterConstant(nameof(Width), 1.0, 221);

        public double Height
        {
            get => (double)GetValue(HeightProperty);
            set => SetValue(HeightProperty, value);
        }

        public static readonly DependencyProperty HeightProperty = RegisterConstant(nameof(Height), 1.0, 222);

        private static DependencyProperty RegisterConstant(string name, double defaultValue, int register)
        {
            return DependencyProperty.Register(
                name,
                typeof(double),
                typeof(RuntimeHlslEffect),
                new UIPropertyMetadata(defaultValue, PixelShaderConstantCallback(register)));
        }
    }
}
