using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GraphCalculator
{
    public enum CurveExtrapolationMode
    {
        Clamp,
        Continue,
        Repeat,
        PingPong,
        Invert,
        RepeatOffset
    }

    public sealed class CurveKey : INotifyPropertyChanged
    {
        private double _x;
        private double _y;
        private double _inTangent;
        private double _outTangent;
        private double _inWeight = 1;
        private double _outWeight = 1;
        private bool _linkedTangents = true;
        private string _tangentMode = "Auto";
        private bool _syncingTangents;

        public Guid Id { get; set; } = Guid.NewGuid();

        public double X
        {
            get => _x;
            set { if (Math.Abs(_x - value) < 1e-12) return; _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { if (Math.Abs(_y - value) < 1e-12) return; _y = value; OnPropertyChanged(); }
        }

        public double InTangent
        {
            get => _inTangent;
            set
            {
                if (Math.Abs(_inTangent - value) < 1e-12) return;
                _inTangent = value;
                OnPropertyChanged();
                if (_linkedTangents && !_syncingTangents)
                {
                    _syncingTangents = true;
                    _outTangent = value;
                    OnPropertyChanged(nameof(OutTangent));
                    _syncingTangents = false;
                }
            }
        }

        public double OutTangent
        {
            get => _outTangent;
            set
            {
                if (Math.Abs(_outTangent - value) < 1e-12) return;
                _outTangent = value;
                OnPropertyChanged();
                if (_linkedTangents && !_syncingTangents)
                {
                    _syncingTangents = true;
                    _inTangent = value;
                    OnPropertyChanged(nameof(InTangent));
                    _syncingTangents = false;
                }
            }
        }

        public double InWeight
        {
            get => _inWeight;
            set { double next = Math.Clamp(double.IsFinite(value) ? value : 1, 0, 4); if (Math.Abs(_inWeight - next) < 1e-12) return; _inWeight = next; OnPropertyChanged(); }
        }

        public double OutWeight
        {
            get => _outWeight;
            set { double next = Math.Clamp(double.IsFinite(value) ? value : 1, 0, 4); if (Math.Abs(_outWeight - next) < 1e-12) return; _outWeight = next; OnPropertyChanged(); }
        }

        public bool LinkedTangents
        {
            get => _linkedTangents;
            set { if (_linkedTangents == value) return; _linkedTangents = value; OnPropertyChanged(); }
        }

        public string TangentMode
        {
            get => _tangentMode;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "Auto" : value;
                if (_tangentMode == next) return;
                _tangentMode = next;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public readonly record struct CurveResolvedKey(CurveKey Key, double InSlope, double OutSlope);

    public sealed record CurveFunctionSuggestion(
        string Name,
        string Formula,
        double Rmse,
        double NormalizedError,
        string Description)
    {
        public string ErrorText => NormalizedError switch
        {
            < 0.005 => $"Excellent · {NormalizedError:P2}",
            < 0.02 => $"Very close · {NormalizedError:P2}",
            < 0.06 => $"Close · {NormalizedError:P1}",
            < 0.15 => $"Approximate · {NormalizedError:P1}",
            _ => $"Loose · {NormalizedError:P1}"
        };
    }

    public static class CurveDesignerMath
    {
        public static double AutoTension { get; set; }
        public static List<CurveKey> Ordered(IEnumerable<CurveKey> keys) =>
            keys.OrderBy(k => k.X).ThenBy(k => k.Id).ToList();

        public static bool HasDistinctX(IReadOnlyList<CurveKey> keys)
        {
            for (int i = 1; i < keys.Count; i++)
                if (Math.Abs(keys[i].X - keys[i - 1].X) < 1e-9) return false;
            return true;
        }

        public static List<CurveResolvedKey> ResolveTangents(IReadOnlyList<CurveKey> ordered)
        {
            var result = new List<CurveResolvedKey>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                CurveKey key = ordered[i];
                double previousSlope = i > 0 ? Secant(ordered[i - 1], key) : (i + 1 < ordered.Count ? Secant(key, ordered[i + 1]) : 0);
                double nextSlope = i + 1 < ordered.Count ? Secant(key, ordered[i + 1]) : previousSlope;
                double autoSlope = i > 0 && i + 1 < ordered.Count
                    ? SafeSlope(ordered[i - 1], ordered[i + 1])
                    : (i == 0 ? nextSlope : previousSlope);
                autoSlope *= 1.0 - Math.Clamp(AutoTension, 0, 1);

                (double incoming, double outgoing) = key.TangentMode switch
                {
                    "Flat" => (0, 0),
                    "Linear" => (previousSlope, nextSlope),
                    "Manual" => (key.InTangent, key.OutTangent),
                    _ => (autoSlope, autoSlope)
                };
                result.Add(new CurveResolvedKey(key, incoming * key.InWeight, outgoing * key.OutWeight));
            }
            return result;
        }

        public static double Evaluate(IReadOnlyList<CurveKey> source, double x) =>
            Evaluate(source, x, CurveExtrapolationMode.Clamp, CurveExtrapolationMode.Clamp);

        public static double Evaluate(
            IReadOnlyList<CurveKey> source,
            double x,
            CurveExtrapolationMode before,
            CurveExtrapolationMode after)
        {
            List<CurveKey> keys = Ordered(source);
            if (keys.Count == 0) return double.NaN;
            if (keys.Count == 1) return keys[0].Y;
            if (!HasDistinctX(keys)) return double.NaN;

            List<CurveResolvedKey> tangents = ResolveTangents(keys);
            return EvaluateResolved(tangents, x, before, after);
        }

        public static List<GraphPoint> Sample(IReadOnlyList<CurveKey> source, int samplesPerSegment = 64)
        {
            List<CurveKey> keys = Ordered(source);
            var result = new List<GraphPoint>();
            if (keys.Count == 0) return result;
            if (keys.Count == 1) { result.Add(new GraphPoint(keys[0].X, keys[0].Y)); return result; }
            if (!HasDistinctX(keys)) return result;

            List<CurveResolvedKey> tangents = ResolveTangents(keys);
            samplesPerSegment = Math.Clamp(samplesPerSegment, 8, 256);
            for (int i = 0; i < keys.Count - 1; i++)
            {
                double x0 = keys[i].X;
                double x1 = keys[i + 1].X;
                for (int j = 0; j <= samplesPerSegment; j++)
                {
                    if (i > 0 && j == 0) continue;
                    double x = x0 + (x1 - x0) * j / samplesPerSegment;
                    result.Add(new GraphPoint(x, EvaluateSegment(tangents[i], tangents[i + 1], x)));
                }
            }
            return result;
        }

        public static List<GraphPoint> SampleRange(
            IReadOnlyList<CurveKey> source,
            double minX,
            double maxX,
            CurveExtrapolationMode before,
            CurveExtrapolationMode after,
            int samples = 720)
        {
            var result = new List<GraphPoint>();
            if (!double.IsFinite(minX) || !double.IsFinite(maxX) || maxX <= minX) return result;
            List<CurveKey> keys = Ordered(source);
            if (keys.Count < 2 || !HasDistinctX(keys)) return Sample(keys);

            List<CurveResolvedKey> tangents = ResolveTangents(keys);
            samples = Math.Clamp(samples, 96, 2400);
            for (int i = 0; i <= samples; i++)
            {
                double x = minX + (maxX - minX) * i / samples;
                result.Add(new GraphPoint(x, EvaluateResolved(tangents, x, before, after)));
            }
            return result;
        }

        public static string BuildCompactDefinition(
            IReadOnlyList<CurveKey> source,
            CurveExtrapolationMode before,
            CurveExtrapolationMode after)
        {
            List<CurveKey> keys = Ordered(source);
            if (keys.Count == 0) return string.Empty;
            if (keys.Count == 1) return $"Constant curve · y = {FShort(keys[0].Y)}";
            return $"Hermite spline · {keys.Count} keys · x {FShort(keys[0].X)} → {FShort(keys[^1].X)} · before: {Friendly(before)} · after: {Friendly(after)}";
        }

        public static string BuildPiecewiseFormula(IReadOnlyList<CurveKey> source) =>
            BuildPiecewiseFormula(source, CurveExtrapolationMode.Clamp, CurveExtrapolationMode.Clamp);

        public static string BuildPiecewiseFormula(
            IReadOnlyList<CurveKey> source,
            CurveExtrapolationMode before,
            CurveExtrapolationMode after)
        {
            List<CurveKey> keys = Ordered(source);
            if (keys.Count == 0) return string.Empty;
            if (keys.Count == 1) return F(keys[0].Y);
            if (!HasDistinctX(keys)) return "/* duplicate x values */";

            List<CurveResolvedKey> tangents = ResolveTangents(keys);
            double a = keys[0].X;
            double b = keys[^1].X;
            string core = BuildCoreFormula(tangents, "x");
            string left = BuildOutsideFormula(tangents, before, true);
            string right = BuildOutsideFormula(tangents, after, false);
            return $"if(x<{F(a)},{left},if(x>{F(b)},{right},{core}))";
        }

        public static IReadOnlyList<CurveFunctionSuggestion> SuggestFunctions(IReadOnlyList<CurveKey> source, int maxSuggestions = 7)
        {
            List<CurveKey> keys = Ordered(source);
            if (keys.Count < 2 || !HasDistinctX(keys)) return Array.Empty<CurveFunctionSuggestion>();

            const int sampleCount = 129;
            double minX = keys[0].X;
            double maxX = keys[^1].X;
            double span = maxX - minX;
            if (span <= 1e-12) return Array.Empty<CurveFunctionSuggestion>();

            List<CurveResolvedKey> tangents = ResolveTangents(keys);
            var xs = new double[sampleCount];
            var ys = new double[sampleCount];
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;
            for (int i = 0; i < sampleCount; i++)
            {
                double x = minX + span * i / (sampleCount - 1);
                double y = EvaluateInside(tangents, x);
                xs[i] = x;
                ys[i] = y;
                if (double.IsFinite(y))
                {
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            double yRange = double.IsFinite(minY) && double.IsFinite(maxY) ? Math.Max(maxY - minY, 1e-9) : 1;
            var candidates = new List<CurveFunctionSuggestion>();

            AddPolynomial(candidates, "Linear", xs, ys, 1, yRange, "Straight-line approximation");
            AddPolynomial(candidates, "Quadratic", xs, ys, 2, yRange, "Single quadratic over the whole authored range");
            AddPolynomial(candidates, "Cubic", xs, ys, 3, yRange, "Single cubic; often a useful compact replacement for a simple spline");

            double y0 = ys[0];
            double y1 = ys[^1];
            AddFixed(candidates, "Smoothstep", xs, ys,
                x => y0 + (y1 - y0) * SmoothStep(minX, maxX, x),
                $"{FShort(y0)}+({FShort(y1 - y0)})*smoothstep({FShort(minX)},{FShort(maxX)},x)",
                yRange,
                "Engine-style cubic ease between the first and last key");
            AddFixed(candidates, "Smootherstep", xs, ys,
                x => y0 + (y1 - y0) * SmootherStep(minX, maxX, x),
                $"{FShort(y0)}+({FShort(y1 - y0)})*smootherstep({FShort(minX)},{FShort(maxX)},x)",
                yRange,
                "Quintic ease with flatter endpoint acceleration");

            AddSineCandidates(candidates, xs, ys, minX, maxX, yRange);
            AddExponentialCandidates(candidates, xs, ys, minX, maxX, yRange);
            AddPowerCandidates(candidates, xs, ys, minX, maxX, yRange);
            AddSigmoidCandidates(candidates, xs, ys, minX, maxX, yRange);
            AddDampedSineCandidates(candidates, xs, ys, minX, maxX, yRange);

            return candidates
                .Where(c => double.IsFinite(c.Rmse))
                .GroupBy(c => c.Formula, StringComparer.Ordinal)
                .Select(g => g.OrderBy(c => c.NormalizedError).First())
                .OrderBy(c => c.NormalizedError + ComplexityPenalty(c.Name))
                .Take(Math.Clamp(maxSuggestions, 1, 12))
                .ToArray();
        }

        private static double EvaluateResolved(
            IReadOnlyList<CurveResolvedKey> tangents,
            double x,
            CurveExtrapolationMode before,
            CurveExtrapolationMode after)
        {
            double minX = tangents[0].Key.X;
            double maxX = tangents[^1].Key.X;
            if (x < minX) return EvaluateOutside(tangents, x, before, beforeRange: true);
            if (x > maxX) return EvaluateOutside(tangents, x, after, beforeRange: false);
            return EvaluateInside(tangents, x);
        }

        private static double EvaluateOutside(
            IReadOnlyList<CurveResolvedKey> tangents,
            double x,
            CurveExtrapolationMode mode,
            bool beforeRange)
        {
            CurveResolvedKey first = tangents[0];
            CurveResolvedKey last = tangents[^1];
            double a = first.Key.X;
            double b = last.Key.X;
            double length = b - a;
            if (length <= 1e-12) return first.Key.Y;

            return mode switch
            {
                CurveExtrapolationMode.Continue => beforeRange
                    ? first.Key.Y + first.InSlope * (x - a)
                    : last.Key.Y + last.OutSlope * (x - b),
                CurveExtrapolationMode.Repeat => EvaluateInside(tangents, a + Repeat(x - a, length)),
                CurveExtrapolationMode.PingPong => EvaluateInside(tangents, a + PingPong(x - a, length)),
                CurveExtrapolationMode.Invert => EvaluateInvertedRepeat(tangents, x),
                CurveExtrapolationMode.RepeatOffset => EvaluateRepeatOffset(tangents, x),
                _ => beforeRange ? first.Key.Y : last.Key.Y
            };
        }

        private static double EvaluateInside(IReadOnlyList<CurveResolvedKey> tangents, double x)
        {
            if (x <= tangents[0].Key.X) return tangents[0].Key.Y;
            if (x >= tangents[^1].Key.X) return tangents[^1].Key.Y;
            int segment = 0;
            while (segment + 1 < tangents.Count && x > tangents[segment + 1].Key.X) segment++;
            return EvaluateSegment(tangents[segment], tangents[segment + 1], x);
        }

        private static double EvaluateInvertedRepeat(IReadOnlyList<CurveResolvedKey> tangents, double x)
        {
            double a = tangents[0].Key.X;
            double b = tangents[^1].Key.X;
            double length = b - a;
            double cycle = Math.Floor((x - a) / length);
            double localX = a + Repeat(x - a, length);
            double y = EvaluateInside(tangents, localX);
            bool odd = Math.Abs(cycle % 2) >= 0.5;
            return odd ? tangents[0].Key.Y + tangents[^1].Key.Y - y : y;
        }

        private static double EvaluateRepeatOffset(IReadOnlyList<CurveResolvedKey> tangents, double x)
        {
            double a = tangents[0].Key.X;
            double b = tangents[^1].Key.X;
            double length = b - a;
            double cycle = Math.Floor((x - a) / length);
            double localX = a + Repeat(x - a, length);
            double deltaY = tangents[^1].Key.Y - tangents[0].Key.Y;
            return EvaluateInside(tangents, localX) + cycle * deltaY;
        }

        private static string BuildCoreFormula(IReadOnlyList<CurveResolvedKey> tangents, string variable)
        {
            if (tangents.Count == 1) return F(tangents[0].Key.Y);
            var pieces = new List<string>();
            for (int i = 0; i < tangents.Count - 1; i++)
                pieces.Add(SegmentFormula(tangents[i], tangents[i + 1], variable));

            string result = F(tangents[^1].Key.Y);
            for (int i = pieces.Count - 1; i >= 0; i--)
                result = $"if(({variable})<{F(tangents[i + 1].Key.X)},{pieces[i]},{result})";
            return result;
        }

        private static string BuildOutsideFormula(
            IReadOnlyList<CurveResolvedKey> tangents,
            CurveExtrapolationMode mode,
            bool beforeRange)
        {
            CurveResolvedKey first = tangents[0];
            CurveResolvedKey last = tangents[^1];
            double a = first.Key.X;
            double b = last.Key.X;
            double length = b - a;
            if (length <= 1e-12) return F(first.Key.Y);

            string localRepeat = $"({F(a)}+repeat((x-({F(a)})),{F(length)}))";
            string localPingPong = $"({F(a)}+pingpong((x-({F(a)})),{F(length)}))";
            return mode switch
            {
                CurveExtrapolationMode.Continue => beforeRange
                    ? $"({F(first.Key.Y)})+({F(first.InSlope)})*(x-({F(a)}))"
                    : $"({F(last.Key.Y)})+({F(last.OutSlope)})*(x-({F(b)}))",
                CurveExtrapolationMode.Repeat => BuildCoreFormula(tangents, localRepeat),
                CurveExtrapolationMode.PingPong => BuildCoreFormula(tangents, localPingPong),
                CurveExtrapolationMode.Invert => BuildInvertFormula(tangents, localRepeat, a, length),
                CurveExtrapolationMode.RepeatOffset => BuildRepeatOffsetFormula(tangents, localRepeat, a, length),
                _ => F(beforeRange ? first.Key.Y : last.Key.Y)
            };
        }

        private static string BuildInvertFormula(IReadOnlyList<CurveResolvedKey> tangents, string localX, double a, double length)
        {
            string core = BuildCoreFormula(tangents, localX);
            string parity = $"mod(floor((x-({F(a)}))/{F(length)}),2)";
            string reflected = $"({F(tangents[0].Key.Y + tangents[^1].Key.Y)})-({core})";
            return $"if({parity}<1,{core},{reflected})";
        }

        private static string BuildRepeatOffsetFormula(IReadOnlyList<CurveResolvedKey> tangents, string localX, double a, double length)
        {
            string core = BuildCoreFormula(tangents, localX);
            string cycle = $"floor((x-({F(a)}))/{F(length)})";
            double delta = tangents[^1].Key.Y - tangents[0].Key.Y;
            return $"({core})+({cycle})*({F(delta)})";
        }

        private static double EvaluateSegment(CurveResolvedKey a, CurveResolvedKey b, double x)
        {
            double h = b.Key.X - a.Key.X;
            if (Math.Abs(h) < 1e-12) return double.NaN;
            double u = Math.Clamp((x - a.Key.X) / h, 0, 1);
            double u2 = u * u;
            double u3 = u2 * u;
            double h00 = 2 * u3 - 3 * u2 + 1;
            double h10 = u3 - 2 * u2 + u;
            double h01 = -2 * u3 + 3 * u2;
            double h11 = u3 - u2;
            return h00 * a.Key.Y + h10 * h * a.OutSlope + h01 * b.Key.Y + h11 * h * b.InSlope;
        }

        private static string SegmentFormula(CurveResolvedKey a, CurveResolvedKey b, string variable)
        {
            double h = b.Key.X - a.Key.X;
            double a3 = (2 * a.Key.Y - 2 * b.Key.Y + h * (a.OutSlope + b.InSlope)) / (h * h * h);
            double a2 = (-3 * a.Key.Y + 3 * b.Key.Y - h * (2 * a.OutSlope + b.InSlope)) / (h * h);
            double a1 = a.OutSlope;
            double a0 = a.Key.Y;
            string s = $"(({variable})-({F(a.Key.X)}))";
            return $"({F(a3)})*{s}^3+({F(a2)})*{s}^2+({F(a1)})*{s}+({F(a0)})";
        }

        private static void AddPolynomial(
            ICollection<CurveFunctionSuggestion> output,
            string name,
            double[] xs,
            double[] ys,
            int degree,
            double yRange,
            string description)
        {
            double[][] basis = Enumerable.Range(0, xs.Length)
                .Select(i => Enumerable.Range(0, degree + 1).Select(power => Math.Pow(xs[i], power)).ToArray())
                .ToArray();
            if (!TryLeastSquares(basis, ys, out double[] c)) return;
            string formula = PolynomialFormula(c);
            AddSuggestion(output, name, formula, xs, ys, x => EvaluatePolynomial(c, x), yRange, description);
        }

        private static void AddSineCandidates(
            ICollection<CurveFunctionSuggestion> output,
            double[] xs,
            double[] ys,
            double minX,
            double maxX,
            double yRange)
        {
            double span = maxX - minX;
            CurveFunctionSuggestion? best = null;
            for (int i = 0; i < 72; i++)
            {
                double cycles = 0.20 + 5.8 * i / 71.0;
                double w = cycles * 2.0 * Math.PI / span;
                double[][] basis = xs.Select(x => new[] { 1.0, Math.Sin(w * x), Math.Cos(w * x) }).ToArray();
                if (!TryLeastSquares(basis, ys, out double[] c)) continue;
                string formula = $"{FShort(c[0])}+({FShort(c[1])})*sin(({FShort(w)})*x)+({FShort(c[2])})*cos(({FShort(w)})*x)";
                CurveFunctionSuggestion suggestion = BuildSuggestion("Sine family", formula, xs, ys,
                    x => c[0] + c[1] * Math.Sin(w * x) + c[2] * Math.Cos(w * x), yRange,
                    $"Best-fitting sinusoid · about {cycles:F2} cycles over the authored range");
                if (best == null || suggestion.NormalizedError < best.NormalizedError) best = suggestion;
            }
            if (best != null) output.Add(best);
        }

        private static void AddExponentialCandidates(
            ICollection<CurveFunctionSuggestion> output,
            double[] xs,
            double[] ys,
            double minX,
            double maxX,
            double yRange)
        {
            double span = maxX - minX;
            double mid = (minX + maxX) * 0.5;
            CurveFunctionSuggestion? best = null;
            for (int i = 0; i < 50; i++)
            {
                double signed = -4.5 + 9.0 * i / 49.0;
                if (Math.Abs(signed) < 0.08) continue;
                double k = signed / span;
                double[][] basis = xs.Select(x => new[] { 1.0, Math.Exp(k * (x - mid)) }).ToArray();
                if (!TryLeastSquares(basis, ys, out double[] c)) continue;
                string formula = $"{FShort(c[0])}+({FShort(c[1])})*exp(({FShort(k)})*(x-({FShort(mid)})))";
                CurveFunctionSuggestion suggestion = BuildSuggestion("Exponential", formula, xs, ys,
                    x => c[0] + c[1] * Math.Exp(k * (x - mid)), yRange,
                    "Offset exponential; useful for growth, decay and soft progression curves");
                if (best == null || suggestion.NormalizedError < best.NormalizedError) best = suggestion;
            }
            if (best != null) output.Add(best);
        }

        private static void AddPowerCandidates(
            ICollection<CurveFunctionSuggestion> output,
            double[] xs,
            double[] ys,
            double minX,
            double maxX,
            double yRange)
        {
            double span = maxX - minX;
            CurveFunctionSuggestion? best = null;
            for (int i = 0; i < 48; i++)
            {
                double p = 0.2 + 5.8 * i / 47.0;
                double[][] basis = xs.Select(x =>
                {
                    double t = Math.Clamp((x - minX) / span, 0, 1);
                    return new[] { 1.0, Math.Pow(t, p) };
                }).ToArray();
                if (!TryLeastSquares(basis, ys, out double[] c)) continue;
                string tExpr = $"saturate((x-({FShort(minX)}))/({FShort(span)}))";
                string formula = $"{FShort(c[0])}+({FShort(c[1])})*({tExpr})^({FShort(p)})";
                CurveFunctionSuggestion suggestion = BuildSuggestion("Power ease", formula, xs, ys,
                    x => c[0] + c[1] * Math.Pow(Math.Clamp((x - minX) / span, 0, 1), p), yRange,
                    $"Normalized power curve · exponent {p:F2}");
                if (best == null || suggestion.NormalizedError < best.NormalizedError) best = suggestion;
            }
            if (best != null) output.Add(best);
        }

        private static void AddSigmoidCandidates(
            ICollection<CurveFunctionSuggestion> output,
            double[] xs,
            double[] ys,
            double minX,
            double maxX,
            double yRange)
        {
            double span = maxX - minX;
            CurveFunctionSuggestion? best = null;
            for (int centerIndex = 0; centerIndex < 9; centerIndex++)
            {
                double center = minX + span * (0.15 + 0.70 * centerIndex / 8.0);
                for (int steepIndex = 0; steepIndex < 30; steepIndex++)
                {
                    double steepness = (0.5 + 11.5 * steepIndex / 29.0) / span;
                    foreach (double sign in new[] { 1.0, -1.0 })
                    {
                        double k = steepness * sign;
                        double[][] basis = xs.Select(x => new[] { 1.0, Logistic(k * (x - center)) }).ToArray();
                        if (!TryLeastSquares(basis, ys, out double[] c)) continue;
                        string formula = $"{FShort(c[0])}+({FShort(c[1])})/(1+exp(-({FShort(k)})*(x-({FShort(center)}))))";
                        CurveFunctionSuggestion suggestion = BuildSuggestion("Sigmoid", formula, xs, ys,
                            x => c[0] + c[1] * Logistic(k * (x - center)), yRange,
                            "Logistic S-curve; often useful for soft caps and difficulty transitions");
                        if (best == null || suggestion.NormalizedError < best.NormalizedError) best = suggestion;
                    }
                }
            }
            if (best != null) output.Add(best);
        }

        private static void AddDampedSineCandidates(
            ICollection<CurveFunctionSuggestion> output,
            double[] xs,
            double[] ys,
            double minX,
            double maxX,
            double yRange)
        {
            double span = maxX - minX;
            CurveFunctionSuggestion? best = null;
            for (int decayIndex = 0; decayIndex < 10; decayIndex++)
            {
                double d = 4.0 * decayIndex / 9.0 / span;
                for (int freqIndex = 0; freqIndex < 34; freqIndex++)
                {
                    double cycles = 0.35 + 4.65 * freqIndex / 33.0;
                    double w = cycles * 2.0 * Math.PI / span;
                    double[][] basis = xs.Select(x =>
                    {
                        double local = x - minX;
                        double e = Math.Exp(-d * local);
                        return new[] { 1.0, e * Math.Sin(w * local), e * Math.Cos(w * local) };
                    }).ToArray();
                    if (!TryLeastSquares(basis, ys, out double[] c)) continue;
                    string localExpr = $"(x-({FShort(minX)}))";
                    string formula = $"{FShort(c[0])}+exp(-({FShort(d)})*{localExpr})*(({FShort(c[1])})*sin(({FShort(w)})*{localExpr})+({FShort(c[2])})*cos(({FShort(w)})*{localExpr}))";
                    CurveFunctionSuggestion suggestion = BuildSuggestion("Damped oscillation", formula, xs, ys,
                        x =>
                        {
                            double local = x - minX;
                            double e = Math.Exp(-d * local);
                            return c[0] + e * (c[1] * Math.Sin(w * local) + c[2] * Math.Cos(w * local));
                        }, yRange,
                        "Damped sine family; useful for recoil, spring motion and VFX responses");
                    if (best == null || suggestion.NormalizedError < best.NormalizedError) best = suggestion;
                }
            }
            if (best != null) output.Add(best);
        }

        private static void AddFixed(
            ICollection<CurveFunctionSuggestion> output,
            string name,
            double[] xs,
            double[] ys,
            Func<double, double> evaluator,
            string formula,
            double yRange,
            string description)
        {
            output.Add(BuildSuggestion(name, formula, xs, ys, evaluator, yRange, description));
        }

        private static void AddSuggestion(
            ICollection<CurveFunctionSuggestion> output,
            string name,
            string formula,
            double[] xs,
            double[] ys,
            Func<double, double> evaluator,
            double yRange,
            string description)
        {
            output.Add(BuildSuggestion(name, formula, xs, ys, evaluator, yRange, description));
        }

        private static CurveFunctionSuggestion BuildSuggestion(
            string name,
            string formula,
            double[] xs,
            double[] ys,
            Func<double, double> evaluator,
            double yRange,
            string description)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < xs.Length; i++)
            {
                double actual = ys[i];
                double predicted = evaluator(xs[i]);
                if (!double.IsFinite(actual) || !double.IsFinite(predicted)) continue;
                double error = predicted - actual;
                sum += error * error;
                count++;
            }
            double rmse = count == 0 ? double.PositiveInfinity : Math.Sqrt(sum / count);
            return new CurveFunctionSuggestion(name, formula, rmse, rmse / Math.Max(yRange, 1e-9), description);
        }

        private static bool TryLeastSquares(double[][] basis, double[] values, out double[] coefficients)
        {
            coefficients = Array.Empty<double>();
            if (basis.Length == 0 || basis.Length != values.Length || basis[0].Length == 0) return false;
            int m = basis[0].Length;
            var augmented = new double[m, m + 1];

            for (int row = 0; row < basis.Length; row++)
            {
                if (!double.IsFinite(values[row])) continue;
                for (int i = 0; i < m; i++)
                {
                    double bi = basis[row][i];
                    if (!double.IsFinite(bi)) return false;
                    for (int j = 0; j < m; j++) augmented[i, j] += bi * basis[row][j];
                    augmented[i, m] += bi * values[row];
                }
            }

            for (int pivot = 0; pivot < m; pivot++)
            {
                int bestRow = pivot;
                double best = Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < m; row++)
                {
                    double candidate = Math.Abs(augmented[row, pivot]);
                    if (candidate > best) { best = candidate; bestRow = row; }
                }
                if (best < 1e-12) return false;
                if (bestRow != pivot)
                {
                    for (int col = pivot; col <= m; col++)
                        (augmented[pivot, col], augmented[bestRow, col]) = (augmented[bestRow, col], augmented[pivot, col]);
                }

                double divisor = augmented[pivot, pivot];
                for (int col = pivot; col <= m; col++) augmented[pivot, col] /= divisor;
                for (int row = 0; row < m; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    for (int col = pivot; col <= m; col++) augmented[row, col] -= factor * augmented[pivot, col];
                }
            }

            coefficients = new double[m];
            for (int i = 0; i < m; i++) coefficients[i] = augmented[i, m];
            return coefficients.All(double.IsFinite);
        }

        private static string PolynomialFormula(IReadOnlyList<double> c)
        {
            var parts = new List<string>();
            for (int power = 0; power < c.Count; power++)
            {
                double value = c[power];
                if (Math.Abs(value) < 1e-12) continue;
                string term = power switch
                {
                    0 => $"({FShort(value)})",
                    1 => $"({FShort(value)})*x",
                    _ => $"({FShort(value)})*x^{power}"
                };
                parts.Add(term);
            }
            return parts.Count == 0 ? "0" : string.Join("+", parts);
        }

        private static double EvaluatePolynomial(IReadOnlyList<double> c, double x)
        {
            double result = 0;
            for (int i = c.Count - 1; i >= 0; i--) result = result * x + c[i];
            return result;
        }

        private static double ComplexityPenalty(string name) => name switch
        {
            "Linear" => 0,
            "Smoothstep" or "Smootherstep" => 0.0005,
            "Quadratic" or "Power ease" or "Exponential" => 0.001,
            "Cubic" or "Sigmoid" or "Sine family" => 0.002,
            _ => 0.003
        };

        private static double SmoothStep(double a, double b, double x)
        {
            if (Math.Abs(b - a) < 1e-12) return x < a ? 0 : 1;
            double t = Math.Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        private static double SmootherStep(double a, double b, double x)
        {
            if (Math.Abs(b - a) < 1e-12) return x < a ? 0 : 1;
            double t = Math.Clamp((x - a) / (b - a), 0, 1);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        private static double Logistic(double x)
        {
            if (x >= 0)
            {
                double e = Math.Exp(-x);
                return 1.0 / (1.0 + e);
            }
            double ep = Math.Exp(x);
            return ep / (1.0 + ep);
        }

        private static double Repeat(double value, double length) =>
            length <= 0 ? 0 : value - Math.Floor(value / length) * length;

        private static double PingPong(double value, double length)
        {
            if (length <= 0) return 0;
            double repeated = Repeat(value, length * 2.0);
            return length - Math.Abs(repeated - length);
        }

        private static double Secant(CurveKey a, CurveKey b) => SafeSlope(a, b);

        private static double SafeSlope(CurveKey a, CurveKey b)
        {
            double dx = b.X - a.X;
            return Math.Abs(dx) < 1e-12 ? 0 : (b.Y - a.Y) / dx;
        }

        private static string Friendly(CurveExtrapolationMode mode) => mode switch
        {
            CurveExtrapolationMode.RepeatOffset => "Repeat + offset",
            CurveExtrapolationMode.PingPong => "Ping-pong",
            CurveExtrapolationMode.Invert => "Invert repeat",
            _ => mode.ToString()
        };

        private static string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        private static string FShort(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
    }
}
