using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    public readonly record struct GraphPoint3D(double X, double Y, double Z);

    public static class ParametricCurveSampler
    {
        public static List<GraphPoint> Sample2D(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            IDictionary<string, double> variables,
            double? minT,
            double? maxT,
            int targetPoints,
            CancellationToken cancellationToken)
        {
            if (components.Count != 2) return [];
            (double start, double end) = ResolveRange(minT, maxT);
            int count = Math.Clamp(targetPoints, 80, 3000);
            var points = new List<GraphPoint>(count);
            var values = new Dictionary<string, double>(variables, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double t = start + (end - start) * i / (count - 1.0);
                values["t"] = t;

                try
                {
                    double x = components[0].Evaluate(values);
                    double y = components[1].Evaluate(values);
                    points.Add(double.IsFinite(x) && double.IsFinite(y)
                        ? new GraphPoint(x, y)
                        : new GraphPoint(double.NaN, double.NaN));
                }
                catch
                {
                    points.Add(new GraphPoint(double.NaN, double.NaN));
                }
            }

            return points;
        }

        public static List<GraphPoint3D> Sample3D(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            IDictionary<string, double> variables,
            double? minT,
            double? maxT,
            int targetPoints,
            CancellationToken cancellationToken)
        {
            if (components.Count != 3) return [];
            (double start, double end) = ResolveRange(minT, maxT);
            int count = Math.Clamp(targetPoints, 80, 1600);
            var points = new List<GraphPoint3D>(count);
            var values = new Dictionary<string, double>(variables, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double t = start + (end - start) * i / (count - 1.0);
                values["t"] = t;

                try
                {
                    double x = components[0].Evaluate(values);
                    double y = components[1].Evaluate(values);
                    double z = components[2].Evaluate(values);
                    points.Add(double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)
                        ? new GraphPoint3D(x, y, z)
                        : new GraphPoint3D(double.NaN, double.NaN, double.NaN));
                }
                catch
                {
                    points.Add(new GraphPoint3D(double.NaN, double.NaN, double.NaN));
                }
            }

            return points;
        }

        public static (double minX, double maxX, double minY, double maxY)? FindBounds2D(
            IReadOnlyList<GraphPoint> points)
        {
            bool any = false;
            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;

            foreach (GraphPoint point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) continue;
                any = true;
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            return any ? (minX, maxX, minY, maxY) : null;
        }

        public static (double minX, double maxX, double minY, double maxY, double minZ, double maxZ)? FindBounds3D(
            IReadOnlyList<GraphPoint3D> points)
        {
            bool any = false;
            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;
            double minZ = double.PositiveInfinity;
            double maxZ = double.NegativeInfinity;

            foreach (GraphPoint3D point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)) continue;
                any = true;
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }

            return any ? (minX, maxX, minY, maxY, minZ, maxZ) : null;
        }

        private static (double start, double end) ResolveRange(double? minT, double? maxT)
        {
            double start = minT ?? 0.0;
            double end = maxT ?? Math.PI * 2.0;
            return start < end ? (start, end) : (0.0, Math.PI * 2.0);
        }
    }
}
