using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    public sealed class ParametricSurfaceSample
    {
        public ParametricSurfaceSample(int columns, int rows, GraphPoint3D[] points)
        {
            Columns = columns;
            Rows = rows;
            Points = points;
        }

        public int Columns { get; }
        public int Rows { get; }
        public GraphPoint3D[] Points { get; }
        public GraphPoint3D this[int column, int row] => Points[row * Columns + column];
    }

    public static class ParametricSurfaceSampler
    {
        public static ParametricSurfaceSample Sample(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            IDictionary<string, double> variables,
            double? minU,
            double? maxU,
            double? minV,
            double? maxV,
            int resolution,
            CancellationToken cancellationToken)
        {
            resolution = Math.Clamp(resolution, 18, 100);
            if (components.Count != 3)
            {
                return new ParametricSurfaceSample(0, 0, []);
            }

            (double u0, double u1) = ResolveRange(minU, maxU, 0, Math.PI * 2.0);
            (double v0, double v1) = ResolveRange(minV, maxV, 0, Math.PI * 2.0);
            var points = new GraphPoint3D[resolution * resolution];
            // Keep u/v inside this sampling pass; shared parameter values should stay untouched.
            var values = new Dictionary<string, double>(variables, StringComparer.OrdinalIgnoreCase);

            for (int row = 0; row < resolution; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double v = v0 + (v1 - v0) * row / (resolution - 1.0);
                values["v"] = v;

                for (int column = 0; column < resolution; column++)
                {
                    double u = u0 + (u1 - u0) * column / (resolution - 1.0);
                    values["u"] = u;

                    try
                    {
                        double x = components[0].Evaluate(values);
                        double y = components[1].Evaluate(values);
                        double z = components[2].Evaluate(values);
                        points[row * resolution + column] = double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)
                            ? new GraphPoint3D(x, y, z)
                            : InvalidPoint;
                    }
                    catch
                    {
                        points[row * resolution + column] = InvalidPoint;
                    }
                }
            }

            return new ParametricSurfaceSample(resolution, resolution, points);
        }

        public static (double minX, double maxX, double minY, double maxY, double minZ, double maxZ)? FindBounds(
            ParametricSurfaceSample sample)
        {
            bool any = false;
            double minX = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;
            double minZ = double.PositiveInfinity;
            double maxZ = double.NegativeInfinity;

            foreach (GraphPoint3D point in sample.Points)
            {
                if (!IsFinite(point)) continue;
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

        public static bool IsFinite(GraphPoint3D point)
        {
            return double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
        }

        private static (double start, double end) ResolveRange(double? minimum, double? maximum, double fallbackMin, double fallbackMax)
        {
            double start = minimum ?? fallbackMin;
            double end = maximum ?? fallbackMax;
            return double.IsFinite(start) && double.IsFinite(end) && start < end
                ? (start, end)
                : (fallbackMin, fallbackMax);
        }

        private static GraphPoint3D InvalidPoint => new(double.NaN, double.NaN, double.NaN);
    }
}
