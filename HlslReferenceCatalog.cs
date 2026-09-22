using System.Collections.Generic;

namespace GraphCalculator
{
    public sealed record ReferenceHelp(string Name, string Signature, string Description, string Category, string HlslNote);

    public static class HlslReferenceCatalog
    {
        public static IReadOnlyList<ReferenceHelp> Items { get; } = new[]
        {
            new ReferenceHelp("float", "float x", "Single floating-point scalar.", "HLSL types", "float roughness = 0.5;"),
            new ReferenceHelp("float2", "float2(x,y)", "Two-component vector. Also a convenient representation for complex numbers.", "HLSL types", "float2 uv = float2(u,v);"),
            new ReferenceHelp("float3", "float3(x,y,z)", "Three-component vector, commonly used for positions, directions and RGB.", "HLSL types", "float3 n = normalize(normal);"),
            new ReferenceHelp("float4", "float4(x,y,z,w)", "Four-component vector, commonly RGBA or homogeneous coordinates.", "HLSL types", "float4 colour = float4(rgb,1.0);"),
            new ReferenceHelp("matrix", "float3x3 / float4x4", "Matrix types. Use mul for explicit vector/matrix multiplication when portability matters.", "HLSL types", "float3 world = mul(local, basis);"),
            new ReferenceHelp("swizzle", "v.xy / v.zyx / colour.rgb", "Read or rearrange vector components without constructing a new vector manually.", "HLSL vectors", "float2 uv = position.xz;"),
            new ReferenceHelp("dot", "dot(a,b)", "Dot product. Useful for projections, lighting and squared complex magnitude.", "HLSL vectors", "float facing = saturate(dot(n,l));"),
            new ReferenceHelp("cross", "cross(a,b)", "3D cross product; returns a vector perpendicular to a and b.", "HLSL vectors", "float3 right = normalize(cross(up,forward));"),
            new ReferenceHelp("length", "length(v)", "Euclidean vector length. For a complex float2 this is |z|.", "HLSL vectors", "float distance = length(p);"),
            new ReferenceHelp("normalize", "normalize(v)", "Return a unit-length vector in the same direction.", "HLSL vectors", "float3 n = normalize(normal);"),
            new ReferenceHelp("reflect", "reflect(i,n)", "Reflect incident direction i around normal n.", "HLSL vectors", "float3 r = reflect(viewDir,n);"),
            new ReferenceHelp("refract", "refract(i,n,eta)", "Refract a direction through a surface using ratio eta.", "HLSL vectors", "float3 r = refract(i,n,eta);"),
            new ReferenceHelp("lerp", "lerp(a,b,t)", "Linear interpolation. Works component-wise on vectors.", "HLSL shaping", "float3 colour = lerp(cold,hot,t);"),
            new ReferenceHelp("saturate", "saturate(x)", "Clamp to 0..1. A very common shader shorthand.", "HLSL shaping", "float mask = saturate(value);"),
            new ReferenceHelp("step", "step(edge,x)", "Hard threshold: 0 below edge, 1 otherwise.", "HLSL shaping", "float mask = step(0.5,noise);"),
            new ReferenceHelp("smoothstep", "smoothstep(a,b,x)", "Smooth Hermite transition from 0 to 1.", "HLSL shaping", "float edge = smoothstep(inner,outer,d);"),
            new ReferenceHelp("frac", "frac(x)", "Fractional part. Useful for tiling procedural coordinates.", "HLSL shaping", "float2 cellUv = frac(uv*8.0);"),
            new ReferenceHelp("fmod", "fmod(x,y)", "Floating remainder. Graph Calculator's mod uses floor-mod semantics, which differ for negative values.", "HLSL shaping", "float r = fmod(x,period);"),
            new ReferenceHelp("pow", "pow(x,p)", "Raise x to p. For complex values use the generated gc_cpow helper.", "HLSL maths", "float shaped = pow(saturate(x),2.2);"),
            new ReferenceHelp("exp/log", "exp(x) / log(x) / log2(x)", "Exponential and logarithmic intrinsics.", "HLSL maths", "float attenuation = exp(-density*d);"),
            new ReferenceHelp("trig", "sin / cos / tan / atan2", "Trigonometric intrinsics use radians.", "HLSL maths", "float angle = atan2(v.y,v.x);"),
            new ReferenceHelp("ddx", "ddx(value)", "Screen-space derivative in the horizontal pixel direction. Only meaningful in pixel/fragment shader contexts.", "HLSL derivatives", "float dx = ddx(height);"),
            new ReferenceHelp("ddy", "ddy(value)", "Screen-space derivative in the vertical pixel direction.", "HLSL derivatives", "float dy = ddy(height);"),
            new ReferenceHelp("fwidth", "fwidth(value)", "Approximately abs(ddx(value))+abs(ddy(value)); excellent for antialiased procedural edges.", "HLSL derivatives", "float aa = fwidth(distanceField);"),
            new ReferenceHelp("for", "for(init; condition; step) { ... }", "Bounded loop. Prefer an obvious maximum bound when targeting real-time shaders.", "HLSL control flow", "[loop] for(int i=0;i<count;i++){ ... }"),
            new ReferenceHelp("unroll", "[unroll] for(...) { ... }", "Ask the compiler to unroll a small, predictable loop. Avoid on very large or dynamic iteration counts.", "HLSL control flow", "[unroll] for(int i=0;i<4;i++){ ... }"),
            new ReferenceHelp("branch", "[branch] if(condition) { ... }", "Hint that a branch may be worth preserving. Divergent branches can still be expensive on a GPU.", "HLSL control flow", "[branch] if(mask>0.5){ ... }"),
            new ReferenceHelp("Texture2D", "Texture2D tex; SamplerState samp", "Modern engine HLSL texture syntax. It is useful for exported code but is newer than the embedded ps_3_0 preview target.", "HLSL textures", "float4 c = tex.Sample(samp,uv);"),
            new ReferenceHelp("sampler2D", "sampler2D tex : register(s0)", "Direct3D 9 style texture sampler used by the embedded ps_3_0 preview.", "HLSL textures", "float4 c = tex2D(tex,uv);"),
            new ReferenceHelp("tex2D", "tex2D(sampler,uv)", "Filtered texture lookup compatible with the embedded ps_3_0 preview.", "HLSL textures", "float4 c = tex2D(tex,uv);"),
            new ReferenceHelp("Sample", "tex.Sample(sampler,uv)", "Filtered texture lookup using normalized UV coordinates.", "HLSL textures", "float3 albedo = tex.Sample(samp,uv).rgb;"),
            new ReferenceHelp("SampleLevel", "tex.SampleLevel(sampler,uv,lod)", "Texture sample with explicit mip level. Useful when implicit derivatives are unavailable.", "HLSL textures", "float4 c = tex.SampleLevel(samp,uv,0);"),
            new ReferenceHelp("Live preview entry", "float4 main(float2 uv : TEXCOORD0) : COLOR0", "Full pixel-shader entry point for the embedded WPF preview.", "Graph Calculator HLSL", "float2 p = gc_mapUv(uv);"),
            new ReferenceHelp("gc_time", "gc_time", "Seconds on the HLSL Lab live clock. Controlled by Play/Pause and Restart.", "Graph Calculator HLSL", "float pulse = 0.5 + 0.5*sin(gc_time);"),
            new ReferenceHelp("gc_mapUv", "gc_mapUv(uv)", "Map normalized preview UV coordinates to the current x/y preview range, with mathematical y pointing upward.", "Graph Calculator HLSL", "float2 p = gc_mapUv(uv);"),
            new ReferenceHelp("gc_resolution", "gc_resolution()", "Return the current live preview width and height in pixels.", "Graph Calculator HLSL", "float2 pixel = 1.0 / gc_resolution();"),
            new ReferenceHelp("ps_3_0", "Compile target: ps_3_0", "The embedded WPF preview runs Direct3D-era pixel shader 3.0 bytecode. Newer engine-only HLSL can still be edited/saved but may not preview here.", "Graph Calculator HLSL", "Use bounded loops and COLOR0/TEXCOORD0 semantics for live preview."),
            new ReferenceHelp("complex multiply", "gc_cmul(a,b)", "Graph Calculator helper for multiplying complex float2 values.", "Graph Calculator HLSL", "float2 z2 = gc_cmul(z,z);"),
            new ReferenceHelp("complex divide", "gc_cdiv(a,b)", "Graph Calculator helper for complex division.", "Graph Calculator HLSL", "float2 q = gc_cdiv(a,b);"),
            new ReferenceHelp("complex power", "gc_cpow(a,b)", "Graph Calculator helper for a^b in the complex plane.", "Graph Calculator HLSL", "float2 w = gc_cpow(z,float2(2,0));"),
            new ReferenceHelp("Mandelbrot", "gc_mandelbrotIter(x,y,count,smooth)", "Generated helper for bounded Mandelbrot escape-time evaluation.", "Graph Calculator HLSL", "float m = gc_mandelbrotIter(x,y,80,1);"),
            new ReferenceHelp("Julia", "gc_juliaIter(x,y,cr,ci,count,smooth)", "Generated helper for bounded Julia escape-time evaluation.", "Graph Calculator HLSL", "float j = gc_juliaIter(x,y,-0.8,0.156,80,1);"),
            new ReferenceHelp("ODE derivative field", "DynamicsDerivative(time,x[,y,z],...)", "Graph Calculator exports ODE/dynamical-system right-hand sides as a derivative function. The numerical trajectory itself is integrated with RK4 on the CPU.", "Graph Calculator HLSL", "float3 d = DynamicsDerivative(time,state.x,state.y,state.z);"),
            new ReferenceHelp("RK4", "state += RK4(derivative,state,time,dt)", "Fourth-order Runge–Kutta integration samples the derivative four times per step. It is accurate enough for many interactive dynamical-system plots, but a shader implementation must choose its own step size and stability trade-offs.", "Numerical methods", "Keep dt bounded; stiff systems may need a different integrator.")
        };
    }
}
