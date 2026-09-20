using System.Collections.Generic;

namespace GraphCalculator
{
    public sealed record PresetExpression(
        string Expression,
        string DomainMinX = "",
        string DomainMaxX = "",
        string DomainMinY = "",
        string DomainMaxY = "");

    public sealed record GraphPreset(
        string Category,
        string Name,
        string Description,
        bool ThreeDimensional,
        IReadOnlyList<PresetExpression> Expressions,
        PlotViewport? PlotView = null,
        SurfaceViewport? SurfaceView = null);

    public static class PresetLibrary
    {
        public static IReadOnlyList<GraphPreset> Items { get; } = new[]
        {
            new GraphPreset(
                "Game design",
                "Diminishing returns",
                "A common stat curve: strong early gains that settle toward a cap.",
                false,
                new[] { new PresetExpression("100*x/(x+20)", "0", "100") },
                new PlotViewport(0, 100, 0, 100)),

            new GraphPreset(
                "Game design",
                "Difficulty ramp",
                "Logistic progression with a soft transition around the midpoint.",
                false,
                new[] { new PresetExpression("100/(1+exp(-0.8*(x-10)))", "0", "20") },
                new PlotViewport(0, 20, 0, 105)),

            new GraphPreset(
                "Game design",
                "Damage falloff",
                "Distance-based falloff with a parameter for tuning the radius.",
                false,
                new[] { new PresetExpression("100/(1+(x/radius)^2)", "0", "30") },
                new PlotViewport(0, 30, 0, 105)),

            new GraphPreset(
                "Game design",
                "Enemy archetype scaler",
                "Grunt, veteran, elite and boss multipliers sharing one difficulty parameter, in that order. Useful for seeing when the upper archetypes begin to pull away from the baseline.",
                false,
                new[]
                {
                    new PresetExpression("1+difficulty*0.06*x", "0", "50"),
                    new PresetExpression("1+difficulty*0.10*x^1.08", "0", "50"),
                    new PresetExpression("1+difficulty*0.14*x^1.15", "0", "50"),
                    new PresetExpression("1+difficulty*0.20*x^1.22", "0", "50")
                },
                new PlotViewport(0, 50, 0, 55)),

            new GraphPreset(
                "Game design",
                "Economy faucet / sink",
                "Income, spending and net pressure on one plot. Change growth to test when a currency starts inflating.",
                false,
                new[]
                {
                    new PresetExpression("baseincome*(1+growth*x)", "0", "30"),
                    new PresetExpression("basespend+0.15*x", "0", "30"),
                    new PresetExpression("baseincome*(1+growth*x)-(basespend+0.15*x)", "0", "30")
                },
                new PlotViewport(0, 30, -10, 35)),

            new GraphPreset(
                "Technical art",
                "Recoil response family",
                "Three damped impulses for comparing snappy, medium and heavy weapon responses in the same view.",
                false,
                new[]
                {
                    new PresetExpression("exp(-0.9*x)*sin(12*x)", "0", "6"),
                    new PresetExpression("0.8*exp(-0.55*x)*sin(8*x)", "0", "6"),
                    new PresetExpression("0.65*exp(-0.32*x)*sin(5*x)", "0", "6")
                },
                new PlotViewport(0, 6, -1.1, 1.1)),

            new GraphPreset(
                "Easing",
                "Interpolation comparison",
                "Linear, smoothstep and smootherstep together for quick response-curve comparisons.",
                false,
                new[]
                {
                    new PresetExpression("x", "0", "1"),
                    new PresetExpression("smoothstep(0,1,x)", "0", "1"),
                    new PresetExpression("smootherstep(0,1,x)", "0", "1")
                },
                new PlotViewport(-0.1, 1.1, -0.1, 1.1)),

            new GraphPreset(
                "Easing",
                "Smoothstep",
                "The standard cubic smoothstep curve over a normalized interval.",
                false,
                new[] { new PresetExpression("smoothstep(0,1,x)", "0", "1") },
                new PlotViewport(-0.1, 1.1, -0.1, 1.1)),

            new GraphPreset(
                "Easing",
                "Smootherstep",
                "A quintic interpolation curve with zero first and second derivatives at both ends.",
                false,
                new[] { new PresetExpression("x^3*(x*(6*x-15)+10)", "0", "1") },
                new PlotViewport(-0.1, 1.1, -0.1, 1.1)),

            new GraphPreset(
                "Technical art",
                "Fresnel power",
                "Edge response using N·V on the horizontal axis.",
                false,
                new[] { new PresetExpression("(1-x)^power", "0", "1") },
                new PlotViewport(0, 1, 0, 1)),

            new GraphPreset(
                "Technical art",
                "Damped impulse",
                "A useful starting point for recoil, spring motion and impact response.",
                false,
                new[] { new PresetExpression("exp(-decay*x)*sin(frequency*x)", "0", "10") },
                new PlotViewport(0, 10, -1.2, 1.2)),

            new GraphPreset(
                "Technical art",
                "Quantization",
                "Posterization/terracing over a normalized input.",
                false,
                new[] { new PresetExpression("floor(steps*x)/steps", "0", "1") },
                new PlotViewport(0, 1, 0, 1)),

            new GraphPreset(
                "Procedural",
                "Value noise slice",
                "A deterministic 1D slice through the built-in 2D value-noise function.",
                false,
                new[] { new PresetExpression("noiseseed(x*frequency,0,seed)", "0", "10") },
                new PlotViewport(0, 10, -1.2, 1.2)),

            new GraphPreset(
                "Parametric",
                "Circle",
                "A basic parametric curve. The domain fields become the t interval.",
                false,
                new[] { new PresetExpression("curve(cos(t),sin(t))", "0", "2*pi") },
                new PlotViewport(-1.3, 1.3, -1.3, 1.3)),

            new GraphPreset(
                "Parametric",
                "Lissajous",
                "Two oscillators with independent frequencies and phase.",
                false,
                new[] { new PresetExpression("curve(sin(a*t+phase),sin(b*t))", "0", "2*pi") },
                new PlotViewport(-1.2, 1.2, -1.2, 1.2)),

            new GraphPreset(
                "3D surfaces",
                "Saddle",
                "Classic hyperbolic paraboloid.",
                true,
                new[] { new PresetExpression("x^2-y^2", "-3", "3", "-3", "3") },
                SurfaceView: new SurfaceViewport(-3, 3, -3, 3, -10, 10)),

            new GraphPreset(
                "3D surfaces",
                "Damped ripple",
                "Radial wave with distance attenuation.",
                true,
                new[] { new PresetExpression("exp(-0.35*sqrt(x^2+y^2))*sin(8*sqrt(x^2+y^2)+t)", "-6", "6", "-6", "6") },
                SurfaceView: new SurfaceViewport(-6, 6, -6, 6, -1.2, 1.2)),

            new GraphPreset(
                "3D surfaces",
                "Procedural terrain",
                "Layered FBM intended for quick displacement experiments.",
                true,
                new[] { new PresetExpression("amplitude*fbm(x*frequency,y*frequency,octaves,persistence,lacunarity)", "-5", "5", "-5", "5") },
                SurfaceView: new SurfaceViewport(-5, 5, -5, 5, -2, 2)),

            new GraphPreset(
                "3D surfaces",
                "Shockwave ring",
                "Radial mask whose radius can be animated directly.",
                true,
                new[] { new PresetExpression("exp(-sharpness*(sqrt(x^2+y^2)-radius)^2)", "-6", "6", "-6", "6") },
                SurfaceView: new SurfaceViewport(-6, 6, -6, 6, 0, 1.1)),

            new GraphPreset(
                "Parametric",
                "3D helix",
                "A spatial curve rendered in the 3D view.",
                true,
                new[] { new PresetExpression("curve3(3*cos(t),3*sin(t),0.35*t)", "-4*pi", "4*pi") },
                SurfaceView: new SurfaceViewport(-4, 4, -4, 4, -5, 5)),


            new GraphPreset(
                "Animation",
                "Travelling wave",
                "A timeline-driven wave; press play and t moves globally rather than becoming another slider.",
                false,
                new[] { new PresetExpression("amplitude*sin(frequency*x+t)", "-2*pi", "2*pi") },
                new PlotViewport(-7, 7, -2.2, 2.2)),

            new GraphPreset(
                "Vector fields",
                "Rotational flow",
                "A simple vortex-like direction field, useful for steering and VFX flow experiments.",
                false,
                new[] { new PresetExpression("field(-y,x)", "-5", "5", "-5", "5") },
                new PlotViewport(-5, 5, -5, 5)),

            new GraphPreset(
                "Vector fields",
                "Sink",
                "Vectors point toward the origin with adjustable strength.",
                false,
                new[] { new PresetExpression("field(-strength*x,-strength*y)", "-5", "5", "-5", "5") },
                new PlotViewport(-5, 5, -5, 5)),

            new GraphPreset(
                "Parametric surfaces",
                "Torus",
                "A true u/v surface; majorRadius and minorRadius remain live parameters.",
                true,
                new[] { new PresetExpression("surface((majorRadius+minorRadius*cos(v))*cos(u),(majorRadius+minorRadius*cos(v))*sin(u),minorRadius*sin(v))", "0", "2*pi", "0", "2*pi") },
                SurfaceView: new SurfaceViewport(-3.5, 3.5, -3.5, 3.5, -2, 2)),

            new GraphPreset(
                "Parametric surfaces",
                "Möbius strip",
                "A one-sided strip that cannot be represented as z = f(x,y).",
                true,
                new[] { new PresetExpression("surface((1+0.35*v*cos(u/2))*cos(u),(1+0.35*v*cos(u/2))*sin(u),0.35*v*sin(u/2))", "0", "2*pi", "-1", "1") },
                SurfaceView: new SurfaceViewport(-1.7, 1.7, -1.7, 1.7, -0.8, 0.8)),

            new GraphPreset(
                "Parametric surfaces",
                "Sphere",
                "A complete sphere as one parametric surface rather than two height fields.",
                true,
                new[] { new PresetExpression("surface(radius*sin(v)*cos(u),radius*sin(v)*sin(u),radius*cos(v))", "0", "2*pi", "0", "pi") },
                SurfaceView: new SurfaceViewport(-3, 3, -3, 3, -3, 3)),

            new GraphPreset(
                "Parametric surfaces",
                "Helicoid",
                "A minimal surface with a tunable vertical pitch.",
                true,
                new[] { new PresetExpression("surface(v*cos(u),v*sin(u),pitch*u)", "-2*pi", "2*pi", "-2", "2") },
                SurfaceView: new SurfaceViewport(-2.5, 2.5, -2.5, 2.5, -3, 3)),

            new GraphPreset(
                "Piecewise",
                "Clamped quadratic",
                "A small piecewise example using comparisons and if(...).",
                false,
                new[] { new PresetExpression("if(x<0,0,if(x>1,1,x^2))", "-1", "2") },
                new PlotViewport(-1, 2, -0.2, 1.2)),

            new GraphPreset(
                "Implicit / SDF",
                "Circle contour",
                "The zero contour of a circle signed-distance field.",
                false,
                new[] { new PresetExpression("implicit(sqrt(x^2+y^2)-radius)", "-4", "4", "-4", "4") },
                new PlotViewport(-4, 4, -4, 4)),

            new GraphPreset(
                "Textures",
                "FBM field",
                "A 2D procedural field. It opens naturally in the texture preview and exports to PNG.",
                false,
                new[] { new PresetExpression("texture(fbm(x*frequency,y*frequency,octaves,persistence,lacunarity))", "-4", "4", "-4", "4") },
                new PlotViewport(-4, 4, -4, 4)),

            new GraphPreset(
                "Implicit / SDF",
                "Sphere isosurface",
                "A true implicit sphere f(x,y,z)=0 rather than a height field.",
                true,
                new[] { new PresetExpression("implicit3(x^2+y^2+z^2-radius^2)", "-4", "4", "-4", "4") },
                SurfaceView: new SurfaceViewport(-4, 4, -4, 4, -4, 4)),

            new GraphPreset(
                "Implicit / SDF",
                "Soft metaballs",
                "Two inverse-square influence fields blended into one implicit surface.",
                true,
                new[] { new PresetExpression("implicit3(1/((x-1.2)^2+y^2+z^2+0.12)+1/((x+1.2)^2+y^2+z^2+0.12)-strength)", "-4", "4", "-4", "4") },
                SurfaceView: new SurfaceViewport(-4, 4, -4, 4, -4, 4)),

            new GraphPreset(
                "Parametric",
                "Trefoil knot",
                "A closed 3D parametric knot, useful for checking spatial curve rendering.",
                true,
                new[] { new PresetExpression("curve3(sin(t)+2*sin(2*t),cos(t)-2*cos(2*t),-sin(3*t))", "0", "2*pi") },
                SurfaceView: new SurfaceViewport(-4, 4, -4, 4, -3, 3))
        };
    }
}
