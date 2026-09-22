using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    public readonly record struct VectorFieldPoint(double X, double Y, double VX, double VY)
    {
        public double Magnitude => Math.Sqrt(VX * VX + VY * VY);
    }

    public static class VectorFieldSampler
    {
        public static List<VectorFieldPoint> Sample(
            IReadOnlyList<CalculatorEngine.CompiledExpression> components,
            PlotViewport viewport,
            IDictionary<string, double> variables,
            double? minX,
            double? maxX,
            double? minY,
            double? maxY,
            int columns,
            int rows,
            CancellationToken cancellationToken)
        {
            var result = new List<VectorFieldPoint>();
            if (components.Count != 2) return result;

            columns = Math.Clamp(columns, 5, 32);
            rows = Math.Clamp(rows, 5, 24);
            var values = new Dictionary<string, double>(variables, StringComparer.OrdinalIgnoreCase);

            double domainMinX = Math.Max(viewport.MinX, minX ?? viewport.MinX);
            double domainMaxX = Math.Min(viewport.MaxX, maxX ?? viewport.MaxX);
            double domainMinY = Math.Max(viewport.MinY, minY ?? viewport.MinY);
            double domainMaxY = Math.Min(viewport.MaxY, maxY ?? viewport.MaxY);
            if (!(domainMinX < domainMaxX) || !(domainMinY < domainMaxY)) return result;

            for (int row = 0; row < rows; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double y = domainMinY + (domainMaxY - domainMinY) * (row + 0.5) / rows;
                values["y"] = y;

                for (int column = 0; column < columns; column++)
                {
                    double x = domainMinX + (domainMaxX - domainMinX) * (column + 0.5) / columns;
                    values["x"] = x;

                    try
                    {
                        double vx = components[0].Evaluate(values);
                        double vy = components[1].Evaluate(values);
                        if (double.IsFinite(vx) && double.IsFinite(vy))
                        {
                            result.Add(new VectorFieldPoint(x, y, vx, vy));
                        }
                    }
                    catch
                    {
                        // A singular arrow is cheaper to ignore than to poison the whole field.
                    }
                }
            }

            return result;
        }
    }
}
