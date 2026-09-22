using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Linq;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private bool _suppressHlslEditorChange;
        private bool _suppressHlslRangeChange;
        private DispatcherTimer? _hlslRuntimeTimer;
        private readonly Stopwatch _hlslRuntimeClock = new();
        private RuntimeHlslEffect? _hlslLiveEffect;
        private bool _hlslRuntimePlaying;
        private double _hlslRuntimeTime;
        private void InitializeMathAndHlslUi()
        {
            if (HlslSourceComboBox != null)
            {
                HlslSourceComboBox.ItemsSource = Expressions;
                if (Expressions.Count > 0) HlslSourceComboBox.SelectedIndex = 0;
            }
            RefreshHlslDictionary();

            _hlslRuntimeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _hlslRuntimeTimer.Tick -= HlslRuntimeTimer_Tick;
            _hlslRuntimeTimer.Tick += HlslRuntimeTimer_Tick;
            UpdateHlslRuntimeControls();
        }

        private void HlslSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (HlslEditorHintText != null) HlslEditorHintText.Text = "Source selected — Translate replaces the editor";
            RefreshHlslPreviewIfEmpty();
            RefreshHlslSourcePreview();
        }

        private void RefreshHlslPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshHlslPreview(markDirty: true);
            if (HlslWorkspaceTabControl != null) HlslWorkspaceTabControl.SelectedIndex = 0;
        }

        private void CopyHlslPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (HlslPreviewTextBox == null || string.IsNullOrWhiteSpace(HlslPreviewTextBox.Text)) return;
            try
            {
                Clipboard.SetText(HlslPreviewTextBox.Text);
                GraphStatusText.Text = "HLSL copied";
            }
            catch (Exception ex)
            {
                GraphStatusText.Text = "Could not copy HLSL: " + ex.Message;
            }
        }

        private void HlslDictionaryFilterTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshHlslDictionary();

        private void RefreshHlslDictionary()
        {
            if (HlslDictionaryListBox == null) return;
            string filter = HlslDictionaryFilterTextBox?.Text?.Trim() ?? string.Empty;
            var items = FunctionCatalog.Items
                .Select(item => new ReferenceHelp(item.Name, item.Signature, item.Description, item.Category, item.HlslNote))
                .Concat(HlslReferenceCatalog.Items)
                .Where(item => filter.Length == 0
                    || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || item.Signature.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || item.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || item.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || item.HlslNote.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            HlslDictionaryListBox.ItemsSource = items;
        }

        private void NewHlslScratchButton_Click(object sender, RoutedEventArgs e)
        {
            if (HlslPreviewTextBox == null) return;
            HlslPreviewTextBox.Text = BlankHlslTemplate;
            if (HlslWorkspaceTabControl != null) HlslWorkspaceTabControl.SelectedIndex = 0;
            HlslPreviewTextBox.Focus();
            HlslPreviewTextBox.CaretIndex = HlslPreviewTextBox.Text.Length;
        }

        private void SaveHlslPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (HlslPreviewTextBox == null || string.IsNullOrWhiteSpace(HlslPreviewTextBox.Text)) return;
            var dialog = new SaveFileDialog
            {
                Title = "Save HLSL",
                Filter = "HLSL shader (*.hlsl)|*.hlsl|Text file (*.txt)|*.txt",
                DefaultExt = ".hlsl",
                FileName = "GraphFunction.hlsl"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, HlslPreviewTextBox.Text);
                GraphStatusText.Text = "HLSL saved";
            }
            catch (Exception ex)
            {
                GraphStatusText.Text = "Could not save HLSL: " + ex.Message;
            }
        }

        private void OpenHlslLabButton_Click(object sender, RoutedEventArgs e)
        {
            SetWorkspaceMode(WorkspaceMode.HlslLab, updateCombo: true);
            RefreshHlslPreviewIfEmpty();
            if (HlslWorkspaceTabControl != null) HlslWorkspaceTabControl.SelectedIndex = 0;
        }

        private void CloseHlslLabButton_Click(object sender, RoutedEventArgs e)
        {
            SetWorkspaceMode(WorkspaceMode.FunctionLab, updateCombo: true);
        }

        private void CompileAndRunHlslButton_Click(object sender, RoutedEventArgs e) => CompileAndRunHlsl();

        private void CompileAndRunHlsl()
        {
            if (HlslPreviewTextBox == null || HlslGpuSurface == null) return;

            Dictionary<string, double> parameters = GetParameterValues();
            if (!HlslLivePreviewSource.TryBuild(HlslPreviewTextBox.Text, parameters, out string source, out string wrapperNote))
            {
                SetHlslCompileStatus(false, wrapperNote);
                return;
            }

            HlslCompileResult result = HlslRuntimeCompiler.CompilePixelShader(source);
            if (!result.Success)
            {
                SetHlslCompileStatus(false, result.Messages);
                return;
            }

            try
            {
                var effect = new RuntimeHlslEffect(result.Bytecode);
                _hlslLiveEffect = effect;
                HlslGpuSurface.Effect = effect;
                ApplyHlslPreviewRange();
                UpdateHlslPreviewResolution();

                _hlslRuntimeTime = 0;
                _hlslRuntimeClock.Restart();
                _hlslRuntimePlaying = true;
                _hlslRuntimeTimer?.Start();
                effect.Time = 0;

                bool ps30 = RenderCapability.IsPixelShaderVersionSupported(3, 0);
                string compilerMessages = string.IsNullOrWhiteSpace(result.Messages) ? "No compiler warnings." : result.Messages;
                string hardware = ps30
                    ? "WPF reports pixel shader 3.0 hardware support."
                    : "WPF does not report pixel shader 3.0 hardware support; the shader compiled but may be ignored at render time.";
                SetHlslCompileStatus(true, $"Compiled and running · ps_3_0 · {result.Bytecode.Length:N0} bytes\n{wrapperNote}\n{hardware}\n{compilerMessages}");
                UpdateHlslRuntimeControls();
                if (HlslWorkspaceTabControl != null) HlslWorkspaceTabControl.SelectedIndex = 1;
            }
            catch (Exception ex)
            {
                SetHlslCompileStatus(false, "The shader compiled, but WPF could not load the bytecode: " + ex.Message);
            }
        }

        private void ToggleHlslPlaybackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hlslLiveEffect == null) return;

            if (_hlslRuntimePlaying)
            {
                _hlslRuntimeTime += _hlslRuntimeClock.Elapsed.TotalSeconds;
                _hlslRuntimeClock.Reset();
                _hlslRuntimePlaying = false;
                _hlslRuntimeTimer?.Stop();
            }
            else
            {
                _hlslRuntimeClock.Restart();
                _hlslRuntimePlaying = true;
                if (_workspaceMode == WorkspaceMode.HlslLab) _hlslRuntimeTimer?.Start();
            }

            UpdateHlslRuntimeControls();
        }

        private void RestartHlslTimeButton_Click(object sender, RoutedEventArgs e)
        {
            _hlslRuntimeTime = 0;
            if (_hlslRuntimePlaying) _hlslRuntimeClock.Restart();
            else _hlslRuntimeClock.Reset();
            if (_hlslLiveEffect != null) _hlslLiveEffect.Time = 0;
            UpdateHlslRuntimeControls();
        }

        private void HlslRuntimeTimer_Tick(object? sender, EventArgs e)
        {
            if (_hlslLiveEffect == null || !_hlslRuntimePlaying || _workspaceMode != WorkspaceMode.HlslLab) return;
            double time = _hlslRuntimeTime + _hlslRuntimeClock.Elapsed.TotalSeconds;
            _hlslLiveEffect.Time = time;
            if (HlslRuntimeTimeText != null) HlslRuntimeTimeText.Text = $"t {time:0.00}s";
            UpdateHlslPreviewResolution();
        }

        private void HlslGpuSurface_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHlslPreviewResolution();

        private void HlslGpuSurface_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (HlslGpuSurface == null || HlslGpuSurface.ActualWidth <= 0 || HlslGpuSurface.ActualHeight <= 0) return;
            if (!TryReadHlslRange(out double minX, out double maxX, out double minY, out double maxY)) return;

            Point cursor = e.GetPosition(HlslGpuSurface);
            double u = Math.Clamp(cursor.X / HlslGpuSurface.ActualWidth, 0.0, 1.0);
            double v = Math.Clamp(cursor.Y / HlslGpuSurface.ActualHeight, 0.0, 1.0);

            // Keep the point under the mouse fixed while the visible range contracts or expands around it.
            double anchorX = minX + (maxX - minX) * u;
            double anchorY = maxY - (maxY - minY) * v;
            double scale = Math.Pow(1.2, -e.Delta / 120.0);

            double nextMinX = anchorX + (minX - anchorX) * scale;
            double nextMaxX = anchorX + (maxX - anchorX) * scale;
            double nextMinY = anchorY + (minY - anchorY) * scale;
            double nextMaxY = anchorY + (maxY - anchorY) * scale;

            // Once double precision can no longer represent a smaller interval, leave the last valid range intact.
            if (!(nextMinX < nextMaxX) || !(nextMinY < nextMaxY)) return;

            _suppressHlslRangeChange = true;
            try
            {
                SetHlslRangeText(HlslMinXTextBox, nextMinX);
                SetHlslRangeText(HlslMaxXTextBox, nextMaxX);
                SetHlslRangeText(HlslMinYTextBox, nextMinY);
                SetHlslRangeText(HlslMaxYTextBox, nextMaxY);
            }
            finally
            {
                _suppressHlslRangeChange = false;
            }

            ApplyHlslPreviewRange();
            e.Handled = true;
        }

        private void HlslPreviewRangeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace || _suppressHlslRangeChange) return;
            ApplyHlslPreviewRange();
        }

        private void UseSourceRangeForHlslButton_Click(object sender, RoutedEventArgs e)
        {
            GraphExpression? source = HlslSourceComboBox?.SelectedItem as GraphExpression;
            source ??= _activeExpressionBox?.DataContext as GraphExpression;
            if (source == null) return;

            Dictionary<string, double> variables = GetParameterValues();
            if (!TryReadExpressionDomain(source, variables, out ExpressionDomain domain, out string? error, includeYOverride: true))
            {
                SetHlslCompileStatus(false, "Could not read the source range: " + error);
                return;
            }

            SetHlslRangeText(HlslMinXTextBox, domain.MinX ?? _viewport.MinX);
            SetHlslRangeText(HlslMaxXTextBox, domain.MaxX ?? _viewport.MaxX);
            SetHlslRangeText(HlslMinYTextBox, domain.MinY ?? _viewport.MinY);
            SetHlslRangeText(HlslMaxYTextBox, domain.MaxY ?? _viewport.MaxY);
            ApplyHlslPreviewRange();
        }

        private void ApplyHlslPreviewRange()
        {
            if (_hlslLiveEffect == null) return;
            if (!TryReadHlslRange(out double minX, out double maxX, out double minY, out double maxY)) return;
            _hlslLiveEffect.MinX = minX;
            _hlslLiveEffect.MaxX = maxX;
            _hlslLiveEffect.MinY = minY;
            _hlslLiveEffect.MaxY = maxY;
        }

        private bool TryReadHlslRange(out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = maxX = minY = maxY = 0;
            return TryReadHlslNumber(HlslMinXTextBox?.Text, out minX)
                && TryReadHlslNumber(HlslMaxXTextBox?.Text, out maxX)
                && TryReadHlslNumber(HlslMinYTextBox?.Text, out minY)
                && TryReadHlslNumber(HlslMaxYTextBox?.Text, out maxY)
                && minX < maxX
                && minY < maxY;
        }

        private static bool TryReadHlslNumber(string? text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static void SetHlslRangeText(TextBox? box, double value)
        {
            // Round-trip formatting matters here: repeated wheel zooms should not lose precision in the text boxes.
            if (box != null) box.Text = value.ToString("G17", CultureInfo.CurrentCulture);
        }

        private void UpdateHlslPreviewResolution()
        {
            if (_hlslLiveEffect == null || HlslGpuSurface == null) return;
            _hlslLiveEffect.Width = Math.Max(1, HlslGpuSurface.ActualWidth);
            _hlslLiveEffect.Height = Math.Max(1, HlslGpuSurface.ActualHeight);
        }

        private void SetHlslCompileStatus(bool success, string text)
        {
            if (HlslCompileStatusText != null)
            {
                HlslCompileStatusText.Text = success ? "Running" : "Compile error";
                HlslCompileStatusText.Foreground = success
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 112))
                    : new SolidColorBrush(Color.FromRgb(210, 83, 83));
            }
            if (HlslCompilerOutputTextBox != null) HlslCompilerOutputTextBox.Text = text;
            if (HlslEditorHintText != null) HlslEditorHintText.Text = success ? "Live GPU preview" : "Fix compiler errors and run again";
            GraphStatusText.Text = success ? "HLSL compiled and running" : "HLSL compile failed";
        }

        private void UpdateHlslRuntimeControls()
        {
            if (HlslPlayPauseButton != null)
            {
                HlslPlayPauseButton.IsEnabled = _hlslLiveEffect != null;
                HlslPlayPauseButton.Content = _hlslRuntimePlaying ? "Pause" : "Play";
            }
            if (HlslRestartTimeButton != null) HlslRestartTimeButton.IsEnabled = _hlslLiveEffect != null;
            if (HlslRuntimeTimeText != null)
            {
                double time = _hlslRuntimeTime + (_hlslRuntimePlaying ? _hlslRuntimeClock.Elapsed.TotalSeconds : 0);
                HlslRuntimeTimeText.Text = $"t {time:0.00}s";
            }
        }

        private void OnHlslWorkspaceVisibilityChanged(bool visible)
        {
            if (_hlslLiveEffect == null || !_hlslRuntimePlaying) return;
            if (visible)
            {
                _hlslRuntimeClock.Restart();
                _hlslRuntimeTimer?.Start();
            }
            else
            {
                _hlslRuntimeTime += _hlslRuntimeClock.Elapsed.TotalSeconds;
                _hlslRuntimeClock.Reset();
                _hlslRuntimeTimer?.Stop();
            }
        }

        private void ShutdownHlslRuntime()
        {
            _hlslRuntimeTimer?.Stop();
            _hlslRuntimeClock.Stop();
            if (HlslGpuSurface != null) HlslGpuSurface.Effect = null;
            _hlslLiveEffect = null;
        }

        private void RefreshHlslSourcePreviewButton_Click(object sender, RoutedEventArgs e) => RefreshHlslSourcePreview();

        private void RefreshHlslSourcePreview()
        {
            if (HlslSourcePreviewImage == null || HlslSourcePreviewStatusText == null) return;
            GraphExpression? source = HlslSourceComboBox?.SelectedItem as GraphExpression;
            source ??= _activeExpressionBox?.DataContext as GraphExpression;

            if (source == null || !source.CompiledParts.Any())
            {
                HlslSourcePreviewImage.Source = null;
                HlslSourcePreviewStatusText.Text = "Select a valid Function Lab expression to preview its source.";
                return;
            }

            try
            {
                Dictionary<string, double> variables = GetParameterValues();
                if (!TryReadExpressionDomain(source, variables, out ExpressionDomain domain, out string? rangeError, includeYOverride: true))
                {
                    HlslSourcePreviewImage.Source = null;
                    HlslSourcePreviewStatusText.Text = "Preview range: " + rangeError;
                    return;
                }

                const int width = 720;
                const int height = 440;
                BitmapSource? bitmap = null;

                if (source.Kind == GraphExpressionKind.ComplexField2D && source.Components.Count > 0)
                {
                    bitmap = BuildComplexFieldBitmap(source.Components[0], variables, domain, width, height, cropToDomain: false);
                    HlslSourcePreviewStatusText.Text = "CPU source preview · complex domain colouring";
                }
                else if ((source.Kind is GraphExpressionKind.TextureField2D or GraphExpressionKind.Implicit2D) && source.Components.Count > 0)
                {
                    bitmap = BuildFieldBitmap(source.Components[0], variables, domain, width, height, GetFieldPaletteName(), cropToDomain: false);
                    HlslSourcePreviewStatusText.Text = "CPU source preview · scalar field (not arbitrary edited HLSL)";
                }
                else if (source.Kind == GraphExpressionKind.Scalar && source.Compiled != null)
                {
                    if (source.Compiled.DependsOnY)
                    {
                        bitmap = BuildFieldBitmap(source.Compiled, variables, domain, width, height, GetFieldPaletteName(), cropToDomain: false);
                        HlslSourcePreviewStatusText.Text = "CPU source preview · f(x,y) field";
                    }
                    else
                    {
                        List<GraphPoint> points = GraphSampler.Sample(source.Compiled, _viewport, width, height, variables, domain.MinX, domain.MaxX, CancellationToken.None);
                        bitmap = BuildCurvePreviewBitmap(points, source.Color, width, height);
                        HlslSourcePreviewStatusText.Text = "CPU source preview · y=f(x)";
                    }
                }
                else if (source.Kind == GraphExpressionKind.Parametric2D)
                {
                    List<GraphPoint> points = ParametricCurveSampler.Sample2D(source.Components, variables, domain.MinX, domain.MaxX, 1200, CancellationToken.None);
                    bitmap = BuildCurvePreviewBitmap(points, source.Color, width, height);
                    HlslSourcePreviewStatusText.Text = "CPU source preview · parametric curve; multi-output HLSL export remains manual";
                }
                else if (source.Kind == GraphExpressionKind.DifferentialEquation1D)
                {
                    List<GraphPoint> points = DynamicalSystemSampler.Sample1D(source.Components, variables, domain.MinX, domain.MaxX, 1400, CancellationToken.None);
                    bitmap = BuildCurvePreviewBitmap(points, source.Color, width, height, fitPoints: true);
                    HlslSourcePreviewStatusText.Text = "CPU source preview · integrated ODE trajectory; generated HLSL exposes the derivative, not the RK4 solver";
                }
                else if (source.Kind == GraphExpressionKind.DynamicalSystem2D)
                {
                    List<GraphPoint> points = DynamicalSystemSampler.Sample2D(source.Components, variables, domain.MinX, domain.MaxX, 1600, CancellationToken.None);
                    bitmap = BuildCurvePreviewBitmap(points, source.Color, width, height, fitPoints: true);
                    HlslSourcePreviewStatusText.Text = "CPU source preview · 2D phase trajectory";
                }
                else if (source.Kind is GraphExpressionKind.Parametric3D or GraphExpressionKind.DynamicalSystem3D)
                {
                    List<GraphPoint3D> points3 = source.Kind == GraphExpressionKind.DynamicalSystem3D
                        ? DynamicalSystemSampler.Sample3D(source.Components, variables, domain.MinX, domain.MaxX, 1800, CancellationToken.None)
                        : ParametricCurveSampler.Sample3D(source.Components, variables, domain.MinX, domain.MaxX, 1400, CancellationToken.None);
                    var projection = new List<GraphPoint>(points3.Count);
                    foreach (GraphPoint3D point in points3)
                    {
                        projection.Add(double.IsFinite(point.X) && double.IsFinite(point.Y)
                            ? new GraphPoint(point.X, point.Y)
                            : new GraphPoint(double.NaN, double.NaN));
                    }
                    bitmap = BuildCurvePreviewBitmap(projection, source.Color, width, height, fitPoints: true);
                    HlslSourcePreviewStatusText.Text = "CPU source preview · XY projection of the 3D source; use Function Lab 3D for the full trajectory";
                }
                else
                {
                    HlslSourcePreviewImage.Source = null;
                    HlslSourcePreviewStatusText.Text = "This source is a 3D surface or another multi-output plot. Preview it in Function Lab; HLSL Lab still exposes translation/reference tools.";
                    return;
                }

                HlslSourcePreviewImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                HlslSourcePreviewImage.Source = null;
                HlslSourcePreviewStatusText.Text = "Could not preview source: " + ex.Message;
            }
        }

        private BitmapSource BuildCurvePreviewBitmap(
            IReadOnlyList<GraphPoint> points,
            Brush brush,
            int width,
            int height,
            bool fitPoints = false)
        {
            PlotViewport view = _viewport;
            if (fitPoints)
            {
                var bounds = ParametricCurveSampler.FindBounds2D(points);
                if (bounds.HasValue)
                {
                    double dx = Math.Max(bounds.Value.maxX - bounds.Value.minX, 1e-6);
                    double dy = Math.Max(bounds.Value.maxY - bounds.Value.minY, 1e-6);
                    view = new PlotViewport(
                        bounds.Value.minX - dx * 0.08, bounds.Value.maxX + dx * 0.08,
                        bounds.Value.minY - dy * 0.08, bounds.Value.maxY + dy * 0.08);
                }
            }

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(ThemeBrush("GraphBackgroundBrush", Brushes.White), null, new Rect(0, 0, width, height));
                var axisPen = new Pen(ThemeBrush("AxisBrush", new SolidColorBrush(Color.FromRgb(140, 147, 158))), 1.2);
                if (view.MinX <= 0 && view.MaxX >= 0)
                {
                    double x0 = (0 - view.MinX) / view.Width * width;
                    dc.DrawLine(axisPen, new Point(x0, 0), new Point(x0, height));
                }
                if (view.MinY <= 0 && view.MaxY >= 0)
                {
                    double y0 = height - (0 - view.MinY) / view.Height * height;
                    dc.DrawLine(axisPen, new Point(0, y0), new Point(width, y0));
                }

                var linePen = new Pen(brush, 2.2) { LineJoin = PenLineJoin.Round };
                Point? previous = null;
                foreach (GraphPoint point in points)
                {
                    if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    {
                        previous = null;
                        continue;
                    }

                    double px = (point.X - view.MinX) / view.Width * width;
                    double py = height - (point.Y - view.MinY) / view.Height * height;
                    if (!double.IsFinite(px) || !double.IsFinite(py))
                    {
                        previous = null;
                        continue;
                    }

                    var current = new Point(px, py);
                    if (previous.HasValue) dc.DrawLine(linePen, previous.Value, current);
                    previous = current;
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private void RefreshHlslPreviewIfEmpty()
        {
            if (HlslPreviewTextBox == null) return;
            string text = HlslPreviewTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)
                || text.StartsWith("// Select a valid expression", StringComparison.Ordinal)
                || text.StartsWith("// This plot wrapper has multiple outputs", StringComparison.Ordinal))
            {
                RefreshHlslPreview(markDirty: false);
            }
        }

        private void HlslPreviewTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressHlslEditorChange) return;
            if (!_loadingWorkspace && _productPassReady) MarkWorkspaceEdit();
            if (_hlslLiveEffect != null && HlslEditorHintText != null)
                HlslEditorHintText.Text = "Edited · preview is the last successful compile";
        }

        private void InsertHlslTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (HlslPreviewTextBox == null) return;
            int index = HlslTemplateComboBox?.SelectedIndex ?? 0;
            HlslPreviewTextBox.Text = index switch
            {
                1 => ComplexRecurrenceTemplate,
                2 => SignedDistanceTemplate,
                3 => ProceduralNoiseTemplate,
                4 => DomainColourTemplate,
                _ => BlankHlslTemplate
            };
            if (HlslWorkspaceTabControl != null) HlslWorkspaceTabControl.SelectedIndex = 0;
            HlslPreviewTextBox.Focus();
            HlslPreviewTextBox.CaretIndex = HlslPreviewTextBox.Text.Length;
            HlslEditorHintText.Text = "Editable template · Compile & Run to preview";
        }

        private void HlslDictionaryListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (HlslPreviewTextBox == null || HlslDictionaryListBox?.SelectedItem is not ReferenceHelp item) return;
            string note = $"\n// {item.Signature}\n// {item.Description}\n// HLSL: {item.HlslNote}\n";
            int caret = Math.Clamp(HlslPreviewTextBox.CaretIndex, 0, HlslPreviewTextBox.Text.Length);
            HlslPreviewTextBox.Text = HlslPreviewTextBox.Text.Insert(caret, note);
            HlslPreviewTextBox.CaretIndex = caret + note.Length;
            HlslPreviewTextBox.Focus();
        }

        private const string BlankHlslTemplate = @"// Animated plasma. Compile & Run, then watch gc_time drive it.
float GraphFunction(float x, float y, float time)
{
    float a = sin(x * 3.0 + time);
    float b = sin(y * 4.0 - time * 1.3);
    float c = sin((x + y) * 2.5 + time * 0.7);
    return 0.5 + 0.5 * sin(a + b + c);
}
";

        private const string ComplexRecurrenceTemplate = @"float2 ComplexMul(float2 a, float2 b)
{
    return float2(a.x*b.x - a.y*b.y, a.x*b.y + a.y*b.x);
}

float MandelbrotEscape(float2 c, int iterations)
{
    float2 z = float2(0.0, 0.0);
    int safeCount = clamp(iterations, 1, 128);

    [loop]
    for (int n = 0; n < 128; n++)
    {
        if (n >= safeCount) break;
        z = ComplexMul(z, z) + c;
        if (dot(z, z) > 4.0)
            return (float)(n + 1) / (float)(safeCount + 1);
    }

    return 0.0;
}

float GraphFunction(float x, float y, float time)
{
    int iterations = clamp((int)(24.0 + time * 6.0), 24, 128);
    return MandelbrotEscape(float2(x, y), iterations);
}
";

        private const string SignedDistanceTemplate = @"float SdCircle(float2 p, float radius)
{
    return length(p) - radius;
}

float GraphFunction(float x, float y, float time)
{
    float2 p = float2(x, y);
    float2 a = float2(sin(time) * 0.8, 0.0);
    float2 b = float2(-sin(time) * 0.8, 0.0);
    float d = min(SdCircle(p - a, 0.55), SdCircle(p - b, 0.55));
    return 1.0 - smoothstep(0.0, 0.035, d);
}
";

        private const string ProceduralNoiseTemplate = @"float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f*f*(3.0 - 2.0*f);
    return lerp(lerp(Hash21(i), Hash21(i + float2(1,0)), u.x),
                lerp(Hash21(i + float2(0,1)), Hash21(i + float2(1,1)), u.x), u.y);
}

float GraphFunction(float x, float y, float time)
{
    return ValueNoise(float2(x, y) * 3.0 + float2(time * 0.25, 0.0));
}
";

        private const string DomainColourTemplate = @"float3 HsvToRgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(0.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1,1,1), saturate(p - 1.0), c.y);
}

float3 ColourComplex(float2 z)
{
    float phase = atan2(z.y, z.x);
    float hue = frac(phase / 6.28318530718 + 1.0);
    float magnitude = length(z);
    float value = 1.0 - exp(-0.35 * magnitude);
    return HsvToRgb(float3(hue, 0.85, value));
}

float3 GraphFunction(float x, float y, float time)
{
    float2 z = float2(x, y);
    float2 numerator = float2(z.x*z.x - z.y*z.y - 1.0, 2.0*z.x*z.y);
    float2 denominator = float2(z.x*z.x - z.y*z.y + 1.0, 2.0*z.x*z.y);
    float d = max(dot(denominator, denominator), 1e-6);
    float2 q = float2(dot(numerator, denominator), numerator.y*denominator.x - numerator.x*denominator.y) / d;
    return ColourComplex(q);
}
";

        private void SetHlslEditorText(string text, bool markDirty)
        {
            if (HlslPreviewTextBox == null) return;
            _suppressHlslEditorChange = true;
            try { HlslPreviewTextBox.Text = text; }
            finally { _suppressHlslEditorChange = false; }
            if (markDirty && !_loadingWorkspace && _productPassReady) MarkWorkspaceEdit();
        }

        private void RefreshHlslPreview(bool markDirty = false)
        {
            if (HlslPreviewTextBox == null) return;
            GraphExpression? source = HlslSourceComboBox?.SelectedItem as GraphExpression;
            source ??= _activeExpressionBox?.DataContext as GraphExpression;
            source ??= Expressions.FirstOrDefault(expression => expression.IsVisible && expression.CompiledParts.Any());

            if (source == null || !source.CompiledParts.Any())
            {
                SetHlslEditorText("// Select a valid expression to see its shader translation.", markDirty);
                return;
            }

            if (source.Kind is GraphExpressionKind.DifferentialEquation1D or GraphExpressionKind.DynamicalSystem2D or GraphExpressionKind.DynamicalSystem3D)
            {
                SetHlslEditorText(BuildDynamicsHlslExport(source), markDirty);
                if (HlslEditorHintText != null) HlslEditorHintText.Text = "Generated derivative function · integration stays explicit";
                return;
            }

            if (source.Kind is GraphExpressionKind.Parametric2D or GraphExpressionKind.Parametric3D or GraphExpressionKind.ParametricSurface3D or GraphExpressionKind.VectorField2D)
            {
                SetHlslEditorText("// This plot wrapper has multiple outputs.\n// Export one component as a scalar expression, or write the corresponding float2/float3 function here.", markDirty);
                return;
            }

            try
            {
                SetHlslEditorText(BuildHlslExport(source), markDirty);
                if (HlslEditorHintText != null) HlslEditorHintText.Text = "Generated from calculator expression";
            }
            catch (NotSupportedException ex)
            {
                if (HlslEditorHintText != null) HlslEditorHintText.Text = "Manual shader implementation required";
                SetHlslEditorText("// CPU expression is valid, but this construct has no automatic generic HLSL lowering yet.\n"
                    + "// " + ex.Message + "\n\n"
                    + "// Use the dictionary tab for the matching HLSL primitive/helper and write the loop explicitly when needed.", markDirty);
            }
            catch (Exception ex)
            {
                SetHlslEditorText("// Could not translate the selected expression.\n// " + ex.Message, markDirty);
            }
        }
        private string BuildDynamicsHlslExport(GraphExpression source)
        {
            int dimensions = source.Kind switch
            {
                GraphExpressionKind.DifferentialEquation1D => 1,
                GraphExpressionKind.DynamicalSystem2D => 2,
                GraphExpressionKind.DynamicalSystem3D => 3,
                _ => throw new InvalidOperationException("Not a dynamical system")
            };
            if (source.Components.Count < dimensions) throw new InvalidOperationException("Incomplete dynamical system");

            string[] bodies = new string[dimensions];
            var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dimensions; i++)
            {
                bodies[i] = source.Components[i].ToHlsl(complexPlane: false);
                bodies[i] = ReplaceHlslIdentifier(bodies[i], "t", "time");
                foreach (string name in source.Components[i].Variables) variables.Add(name);
            }

            string[] parameters = variables
                .Where(name => !name.Equals("x", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("y", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("z", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("t", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("time", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var args = new List<string> { "float time", "float x" };
            if (dimensions >= 2) args.Add("float y");
            if (dimensions >= 3) args.Add("float z");
            args.AddRange(parameters.Select(name => "float " + name));
            string returnType = dimensions switch { 1 => "float", 2 => "float2", _ => "float3" };
            string returnValue = dimensions switch
            {
                1 => bodies[0],
                2 => $"float2({bodies[0]}, {bodies[1]})",
                _ => $"float3({bodies[0]}, {bodies[1]}, {bodies[2]})"
            };

            return "// Graph Calculator dynamical-system export\n"
                + "// Source uses RK4 on the CPU. This HLSL exposes only the derivative field, so a shader/game loop can choose its own integrator.\n"
                + "// Source: " + source.Expression.Replace("\r", " ").Replace("\n", " | ") + "\n\n"
                + returnType + " DynamicsDerivative(" + string.Join(", ", args) + ")\n{\n"
                + "    return " + returnValue + ";\n}\n";
        }

    }
}
