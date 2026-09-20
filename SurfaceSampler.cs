using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    public sealed class SurfaceSample
    {
        public SurfaceSample(int columns, int rows, double[] values)
        {
            Columns = columns;
            Rows = rows;
            Values = values;
        }

        public int Columns { get; }
        public int Rows { get; }
        public double[] Values { get; }

        public double this[int column, int row] => Values[row * Columns + column];
    }

    public static class SurfaceSampler
    {
        public static SurfaceSample Sample(
            CalculatorEngine.CompiledExpression expression,
            SurfaceViewport viewport,
            int resolution,
            CancellationToken cancellationToken)
        {
            resolution = Math.Clamp(resolution, 20, 100);
            var values = new double[resolution * resolution];

            for (int row = 0; row < resolution; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double y = viewport.MinY + viewport.Depth * row / (resolution - 1);

                for (int column = 0; column < resolution; column++)
                {
                    double x = viewport.MinX + viewport.Width * column / (resolution - 1);
                    values[row * resolution + column] = SafeEvaluate(expression, x, y);
                }
            }

            return new SurfaceSample(resolution, resolution, values);
        }

        public static (double minZ, double maxZ)? FindZBounds(
            IEnumerable<CalculatorEngine.CompiledExpression> expressions,
            SurfaceViewport viewport,
            CancellationToken cancellationToken)
        {
            var values = new List<double>();
            const int resolution = 72;

            foreach (CalculatorEngine.CompiledExpression expression in expressions)
            {
                for (int row = 0; row < resolution; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double y = viewport.MinY + viewport.Depth * row / (resolution - 1);

                    for (int column = 0; column < resolution; column++)
                    {
                        double x = viewport.MinX + viewport.Width * column / (resolution - 1);
                        double z = SafeEvaluate(expression, x, y);

                        if (double.IsFinite(z) && Math.Abs(z) < 1e100)
                        {
                            values.Add(z);
                        }
                    }
                }
            }

            if (values.Count == 0) return null;

            values.Sort();
            int lowIndex = values.Count > 200 ? (int)(values.Count * 0.01) : 0;
            int highIndex = values.Count > 200 ? (int)(values.Count * 0.99) : values.Count - 1;

            double minZ = values[Math.Clamp(lowIndex, 0, values.Count - 1)];
            double maxZ = values[Math.Clamp(highIndex, 0, values.Count - 1)];

            if (Math.Abs(maxZ - minZ) < 1e-10)
            {
                double center = (minZ + maxZ) * 0.5;
                double padding = Math.Max(1.0, Math.Abs(center) * 0.15);
                return (center - padding, center + padding);
            }

            double range = maxZ - minZ;
            double pad = range * 0.1;
            return (minZ - pad, maxZ + pad);
        }

        private static double SafeEvaluate(CalculatorEngine.CompiledExpression expression, double x, double y)
        {
            try
            {
                return expression.Evaluate(x, y);
            }
            catch
            {
                return double.NaN;
            }
        }
    }
}
