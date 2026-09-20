using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    public readonly record struct SurfaceBoundaryPoint(double X, double Y, double Z);

    public sealed class SurfaceSample
    {
        private readonly SurfaceBoundaryPoint?[] _horizontalBoundaries;
        private readonly SurfaceBoundaryPoint?[] _verticalBoundaries;

        public SurfaceSample(
            int columns,
            int rows,
            double[] values,
            SurfaceBoundaryPoint?[] horizontalBoundaries,
            SurfaceBoundaryPoint?[] verticalBoundaries)
        {
            Columns = columns;
            Rows = rows;
            Values = values;
            _horizontalBoundaries = horizontalBoundaries;
            _verticalBoundaries = verticalBoundaries;
        }

        public int Columns { get; }
        public int Rows { get; }
        public double[] Values { get; }

        public double this[int column, int row] => Values[row * Columns + column];

        public SurfaceBoundaryPoint? GetHorizontalBoundary(int column, int row)
        {
            if (column < 0 || column >= Columns - 1 || row < 0 || row >= Rows) return null;
            return _horizontalBoundaries[row * (Columns - 1) + column];
        }

        public SurfaceBoundaryPoint? GetVerticalBoundary(int column, int row)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows - 1) return null;
            return _verticalBoundaries[row * Columns + column];
        }
    }

    public readonly record struct SurfaceSeriesSampleRequest(
        CalculatorEngine.CompiledExpression Expression,
        double? MinX,
        double? MaxX,
        double? MinY,
        double? MaxY);

    public static class SurfaceSampler
    {
        private const int BoundaryIterations = 14;

        public static SurfaceSample Sample(
            CalculatorEngine.CompiledExpression expression,
            SurfaceViewport viewport,
            int resolution,
            IDictionary<string, double> variables,
            double? domainMinX,
            double? domainMaxX,
            double? domainMinY,
            double? domainMaxY,
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

                    if (!InsideDomain(x, y, domainMinX, domainMaxX, domainMinY, domainMaxY))
                    {
                        values[row * resolution + column] = double.NaN;
                        continue;
                    }

                    values[row * resolution + column] = SafeEvaluate(expression, x, y, variables);
                }
            }

            var horizontalBoundaries = new SurfaceBoundaryPoint?[resolution * (resolution - 1)];
            var verticalBoundaries = new SurfaceBoundaryPoint?[(resolution - 1) * resolution];

            for (int row = 0; row < resolution; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double y = viewport.MinY + viewport.Depth * row / (resolution - 1);

                for (int column = 0; column < resolution - 1; column++)
                {
                    double z0 = values[row * resolution + column];
                    double z1 = values[row * resolution + column + 1];
                    bool visible0 = IsVisible(z0, viewport);
                    bool visible1 = IsVisible(z1, viewport);
                    if (visible0 == visible1) continue;

                    double x0 = viewport.MinX + viewport.Width * column / (resolution - 1);
                    double x1 = viewport.MinX + viewport.Width * (column + 1) / (resolution - 1);

                    horizontalBoundaries[row * (resolution - 1) + column] = FindBoundaryPoint(
                        expression,
                        viewport,
                        variables,
                        domainMinX,
                        domainMaxX,
                        domainMinY,
                        domainMaxY,
                        x0,
                        y,
                        z0,
                        x1,
                        y,
                        z1,
                        visible0,
                        cancellationToken);
                }
            }

            for (int row = 0; row < resolution - 1; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double y0 = viewport.MinY + viewport.Depth * row / (resolution - 1);
                double y1 = viewport.MinY + viewport.Depth * (row + 1) / (resolution - 1);

                for (int column = 0; column < resolution; column++)
                {
                    double z0 = values[row * resolution + column];
                    double z1 = values[(row + 1) * resolution + column];
                    bool visible0 = IsVisible(z0, viewport);
                    bool visible1 = IsVisible(z1, viewport);
                    if (visible0 == visible1) continue;

                    double x = viewport.MinX + viewport.Width * column / (resolution - 1);

                    verticalBoundaries[row * resolution + column] = FindBoundaryPoint(
                        expression,
                        viewport,
                        variables,
                        domainMinX,
                        domainMaxX,
                        domainMinY,
                        domainMaxY,
                        x,
                        y0,
                        z0,
                        x,
                        y1,
                        z1,
                        visible0,
                        cancellationToken);
                }
            }

            return new SurfaceSample(
                resolution,
                resolution,
                values,
                horizontalBoundaries,
                verticalBoundaries);
        }

        public static (double minZ, double maxZ)? FindZBounds(
            IEnumerable<SurfaceSeriesSampleRequest> expressions,
            SurfaceViewport viewport,
            IDictionary<string, double> variables,
            CancellationToken cancellationToken)
        {
            var values = new List<double>();
            const int resolution = 72;

            foreach (SurfaceSeriesSampleRequest request in expressions)
            {
                for (int row = 0; row < resolution; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double y = viewport.MinY + viewport.Depth * row / (resolution - 1);

                    for (int column = 0; column < resolution; column++)
                    {
                        double x = viewport.MinX + viewport.Width * column / (resolution - 1);
                        if (!InsideDomain(x, y, request.MinX, request.MaxX, request.MinY, request.MaxY)) continue;

                        double z = SafeEvaluate(request.Expression, x, y, variables);

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

        private static SurfaceBoundaryPoint FindBoundaryPoint(
            CalculatorEngine.CompiledExpression expression,
            SurfaceViewport viewport,
            IDictionary<string, double> variables,
            double? domainMinX,
            double? domainMaxX,
            double? domainMinY,
            double? domainMaxY,
            double x0,
            double y0,
            double z0,
            double x1,
            double y1,
            double z1,
            bool firstIsVisible,
            CancellationToken cancellationToken)
        {
            double visibleX;
            double visibleY;
            double visibleZ;
            double hiddenX;
            double hiddenY;

            if (firstIsVisible)
            {
                visibleX = x0;
                visibleY = y0;
                visibleZ = z0;
                hiddenX = x1;
                hiddenY = y1;
            }
            else
            {
                visibleX = x1;
                visibleY = y1;
                visibleZ = z1;
                hiddenX = x0;
                hiddenY = y0;
            }

            for (int i = 0; i < BoundaryIterations; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double midX = (visibleX + hiddenX) * 0.5;
                double midY = (visibleY + hiddenY) * 0.5;
                double midZ = InsideDomain(midX, midY, domainMinX, domainMaxX, domainMinY, domainMaxY)
                    ? SafeEvaluate(expression, midX, midY, variables)
                    : double.NaN;

                if (IsVisible(midZ, viewport))
                {
                    visibleX = midX;
                    visibleY = midY;
                    visibleZ = midZ;
                }
                else
                {
                    hiddenX = midX;
                    hiddenY = midY;
                }
            }

            return new SurfaceBoundaryPoint(visibleX, visibleY, visibleZ);
        }

        private static bool InsideDomain(
            double x,
            double y,
            double? minX,
            double? maxX,
            double? minY,
            double? maxY)
        {
            return (!minX.HasValue || x >= minX.Value)
                && (!maxX.HasValue || x <= maxX.Value)
                && (!minY.HasValue || y >= minY.Value)
                && (!maxY.HasValue || y <= maxY.Value);
        }

        private static bool IsVisible(double z, SurfaceViewport viewport)
        {
            return double.IsFinite(z) && z >= viewport.MinZ && z <= viewport.MaxZ;
        }

        private static double SafeEvaluate(
            CalculatorEngine.CompiledExpression expression,
            double x,
            double y,
            IDictionary<string, double> variables)
        {
            try
            {
                return expression.Evaluate(x, y, variables);
            }
            catch
            {
                return double.NaN;
            }
        }
    }
}
