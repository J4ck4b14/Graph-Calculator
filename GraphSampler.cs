using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    public readonly record struct GraphPoint(double X, double Y);

    public static class GraphSampler
    {
        private const int MaxDepth = 5;
        private const int MaxPoints = 9000;

        public static List<GraphPoint> Sample(
            CalculatorEngine.CompiledExpression expression,
            PlotViewport viewport,
            int pixelWidth,
            int pixelHeight,
            CancellationToken cancellationToken)
        {
            int intervals = Math.Clamp(pixelWidth / 10, 80, 260);
            var points = new List<GraphPoint>(Math.Min(pixelWidth * 2, MaxPoints));

            double x0 = viewport.MinX;
            double y0 = SafeEvaluate(expression, x0);
            points.Add(new GraphPoint(x0, y0));

            for (int i = 1; i <= intervals && points.Count < MaxPoints; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double x1 = viewport.MinX + viewport.Width * i / intervals;
                double y1 = SafeEvaluate(expression, x1);
                SampleInterval(expression, viewport, pixelHeight, x0, y0, x1, y1, 0, points, cancellationToken);
                x0 = x1;
                y0 = y1;
            }

            return points;
        }

        public static (double minY, double maxY)? FindYBounds(
            IEnumerable<CalculatorEngine.CompiledExpression> expressions,
            double minX,
            double maxX,
            CancellationToken cancellationToken)
        {
            var values = new List<double>();
            const int samples = 900;

            foreach (var expression in expressions)
            {
                for (int i = 0; i < samples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double x = minX + (maxX - minX) * i / (samples - 1);
                    double y = SafeEvaluate(expression, x);

                    if (double.IsFinite(y) && Math.Abs(y) < 1e100)
                    {
                        values.Add(y);
                    }
                }
            }

            if (values.Count == 0) return null;

            values.Sort();
            int lowIndex = values.Count > 100 ? (int)(values.Count * 0.01) : 0;
            int highIndex = values.Count > 100 ? (int)(values.Count * 0.99) : values.Count - 1;

            double minY = values[Math.Clamp(lowIndex, 0, values.Count - 1)];
            double maxY = values[Math.Clamp(highIndex, 0, values.Count - 1)];

            if (Math.Abs(maxY - minY) < 1e-10)
            {
                double center = (minY + maxY) * 0.5;
                double padding = Math.Max(1.0, Math.Abs(center) * 0.15);
                return (center - padding, center + padding);
            }

            double range = maxY - minY;
            double pad = range * 0.1;
            return (minY - pad, maxY + pad);
        }

        private static void SampleInterval(
            CalculatorEngine.CompiledExpression expression,
            PlotViewport viewport,
            int pixelHeight,
            double x0,
            double y0,
            double x1,
            double y1,
            int depth,
            List<GraphPoint> points,
            CancellationToken cancellationToken)
        {
            if (points.Count >= MaxPoints) return;
            cancellationToken.ThrowIfCancellationRequested();

            double midX = (x0 + x1) * 0.5;
            double midY = SafeEvaluate(expression, midX);

            if (depth < MaxDepth && ShouldSplit(viewport, pixelHeight, y0, midY, y1))
            {
                SampleInterval(expression, viewport, pixelHeight, x0, y0, midX, midY, depth + 1, points, cancellationToken);
                SampleInterval(expression, viewport, pixelHeight, midX, midY, x1, y1, depth + 1, points, cancellationToken);
                return;
            }

            // Keeping an invalid midpoint in the sample is useful: it breaks the line at domain holes.
            if (!double.IsFinite(midY) && double.IsFinite(y0) && double.IsFinite(y1))
            {
                points.Add(new GraphPoint(midX, double.NaN));
            }

            points.Add(new GraphPoint(x1, y1));
        }

        private static bool ShouldSplit(PlotViewport viewport, int pixelHeight, double y0, double midY, double y1)
        {
            bool f0 = double.IsFinite(y0);
            bool fm = double.IsFinite(midY);
            bool f1 = double.IsFinite(y1);

            if (f0 != fm || fm != f1) return true;
            if (!f0) return false;

            double p0 = ToPixelY(y0, viewport, pixelHeight);
            double pm = ToPixelY(midY, viewport, pixelHeight);
            double p1 = ToPixelY(y1, viewport, pixelHeight);

            double linearMid = (p0 + p1) * 0.5;
            double curveError = Math.Abs(pm - linearMid);
            double bend = Math.Abs((pm - p0) - (p1 - pm));

            return curveError > 0.9 || bend > 5.0;
        }

        private static double ToPixelY(double y, PlotViewport viewport, int pixelHeight)
        {
            return pixelHeight - (y - viewport.MinY) / viewport.Height * pixelHeight;
        }

        private static double SafeEvaluate(CalculatorEngine.CompiledExpression expression, double x)
        {
            try
            {
                return expression.Evaluate(x);
            }
            catch
            {
                return double.NaN;
            }
        }
    }
}
