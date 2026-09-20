using System.Collections.Generic;

namespace GraphCalculator
{
    public sealed record FunctionHelp(string Name, string Signature, string Description);

    public static class FunctionCatalog
    {
        public static IReadOnlyList<FunctionHelp> Items { get; } = new[]
        {
            new FunctionHelp("sin", "sin(x)", "Sine, radians"),
            new FunctionHelp("cos", "cos(x)", "Cosine, radians"),
            new FunctionHelp("tan", "tan(x)", "Tangent, radians"),
            new FunctionHelp("sqrt", "sqrt(x)", "Square root"),
            new FunctionHelp("abs", "abs(x)", "Absolute value"),
            new FunctionHelp("exp", "exp(x)", "e raised to x"),
            new FunctionHelp("clamp", "clamp(x,min,max)", "Clamp to a range"),
            new FunctionHelp("saturate", "saturate(x)", "Clamp to 0..1"),
            new FunctionHelp("lerp", "lerp(a,b,t)", "Linear interpolation"),
            new FunctionHelp("inverselerp", "inverselerp(a,b,x)", "Normalize x between a and b"),
            new FunctionHelp("remap", "remap(inMin,inMax,outMin,outMax,x)", "Map one range into another"),
            new FunctionHelp("step", "step(edge,x)", "0 below the edge, otherwise 1"),
            new FunctionHelp("smoothstep", "smoothstep(a,b,x)", "Cubic smooth transition"),
            new FunctionHelp("smootherstep", "smootherstep(a,b,x)", "Quintic smooth transition"),
            new FunctionHelp("frac", "frac(x)", "Fractional part"),
            new FunctionHelp("mod", "mod(x,y)", "Floor-based modulo"),
            new FunctionHelp("repeat", "repeat(x,length)", "Repeat x over a period"),
            new FunctionHelp("pingpong", "pingpong(x,length)", "Back-and-forth repeating value"),
            new FunctionHelp("if", "if(condition,a,b)", "Piecewise conditional; comparisons return 0 or 1"),
            new FunctionHelp("and", "and(a,b)", "Numeric logical AND"),
            new FunctionHelp("or", "or(a,b)", "Numeric logical OR"),
            new FunctionHelp("not", "not(x)", "Numeric logical NOT"),
            new FunctionHelp("uniform", "uniform(min,max,r)", "Map a 0..1 random sample into a range"),
            new FunctionHelp("normal", "normal(mean,std,g)", "Map a standard-normal sample into a distribution"),
            new FunctionHelp("bernoulli", "bernoulli(p,r)", "1 when sample r succeeds probability p"),
            new FunctionHelp("noise", "noise(x,y)", "Deterministic value noise"),
            new FunctionHelp("noiseseed", "noiseseed(x,y,seed)", "Seeded value noise"),
            new FunctionHelp("fbm", "fbm(x,y,octaves,persistence,lacunarity)", "Layered value noise"),
            new FunctionHelp("curve", "curve(x(t),y(t))", "2D parametric curve"),
            new FunctionHelp("curve3", "curve3(x(t),y(t),z(t))", "3D parametric curve"),
            new FunctionHelp("surface", "surface(x(u,v),y(u,v),z(u,v))", "3D parametric surface"),
            new FunctionHelp("field", "field(vx(x,y),vy(x,y))", "2D vector field"),
            new FunctionHelp("implicit", "implicit(f(x,y))", "2D zero contour f(x,y)=0"),
            new FunctionHelp("implicit3", "implicit3(f(x,y,z))", "3D zero isosurface f(x,y,z)=0"),
            new FunctionHelp("texture", "texture(f(x,y))", "2D scalar field rendered as a texture")
        };
    }
}
