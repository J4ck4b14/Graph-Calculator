using System;
using System.Collections.Generic;
using System.Threading;

namespace GraphCalculator
{
    public readonly record struct GraphSegment(GraphPoint A, GraphPoint B);

    public sealed class ImplicitSurfaceSample
    {
        public List<GraphPoint3D> Vertices { get; } = [];
        public List<int> TriangleIndices { get; } = [];
    }

    public static class ImplicitSampler
    {
        private static readonly (int A, int B)[] SquareEdges =
        {
            (0, 1), (1, 2), (2, 3), (3, 0)
        };

        // Six tetrahedra avoid the giant 256-case marching-cubes table and behave well enough for an inspector.
        private static readonly int[][] CubeTetrahedra =
        {
            [0, 5, 1, 6],
            [0, 1, 2, 6],
            [0, 2, 3, 6],
            [0, 3, 7, 6],
            [0, 7, 4, 6],
            [0, 4, 5, 6]
        };

        public static List<GraphSegment> Sample2D(
            CalculatorEngine.CompiledExpression expression,
            PlotViewport viewport,
            IDictionary<string, double> variables,
            double? domainMinX,
            double? domainMaxX,
            double? domainMinY,
            double? domainMaxY,
            int resolution,
            CancellationToken cancellationToken)
        {
            resolution = Math.Clamp(resolution, 16, 500);
            double minX = Math.Max(viewport.MinX, domainMinX ?? viewport.MinX);
            double maxX = Math.Min(viewport.MaxX, domainMaxX ?? viewport.MaxX);
            double minY = Math.Max(viewport.MinY, domainMinY ?? viewport.MinY);
            double maxY = Math.Min(viewport.MaxY, domainMaxY ?? viewport.MaxY);
            var result = new List<GraphSegment>();
            if (!(minX < maxX) || !(minY < maxY)) return result;

            int size = resolution + 1;
            var values = new double[size, size];
            for (int iy = 0; iy <= resolution; iy++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double y = minY + (maxY - minY) * iy / resolution;
                for (int ix = 0; ix <= resolution; ix++)
                {
                    double x = minX + (maxX - minX) * ix / resolution;
                    values[ix, iy] = Evaluate2D(expression, x, y, variables);
                }
            }

            Span<GraphPoint> crossings = stackalloc GraphPoint[4];
            for (int iy = 0; iy < resolution; iy++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double y0 = minY + (maxY - minY) * iy / resolution;
                double y1 = minY + (maxY - minY) * (iy + 1) / resolution;

                for (int ix = 0; ix < resolution; ix++)
                {
                    double x0 = minX + (maxX - minX) * ix / resolution;
                    double x1 = minX + (maxX - minX) * (ix + 1) / resolution;
                    GraphPoint[] p =
                    {
                        new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)
                    };
                    double[] v =
                    {
                        values[ix, iy], values[ix + 1, iy], values[ix + 1, iy + 1], values[ix, iy + 1]
                    };

                    int crossingCount = 0;
                    foreach ((int a, int b) in SquareEdges)
                    {
                        if (!Crosses(v[a], v[b])) continue;
                        crossings[crossingCount++] = Interpolate(p[a], p[b], v[a], v[b]);
                    }

                    if (crossingCount == 2)
                    {
                        result.Add(new GraphSegment(crossings[0], crossings[1]));
                    }
                    else if (crossingCount == 4)
                    {
                        double center = Evaluate2D(expression, (x0 + x1) * 0.5, (y0 + y1) * 0.5, variables);
                        bool sameAsCorner0 = double.IsFinite(center) && (center < 0) == (v[0] < 0);
                        if (sameAsCorner0)
                        {
                            result.Add(new GraphSegment(crossings[0], crossings[3]));
                            result.Add(new GraphSegment(crossings[1], crossings[2]));
                        }
                        else
                        {
                            result.Add(new GraphSegment(crossings[0], crossings[1]));
                            result.Add(new GraphSegment(crossings[2], crossings[3]));
                        }
                    }
                }
            }

            return result;
        }

        public static ImplicitSurfaceSample Sample3D(
            CalculatorEngine.CompiledExpression expression,
            SurfaceViewport viewport,
            IDictionary<string, double> variables,
            double? domainMinX,
            double? domainMaxX,
            double? domainMinY,
            double? domainMaxY,
            int resolution,
            CancellationToken cancellationToken)
        {
            resolution = Math.Clamp(resolution, 8, 64);
            double minX = Math.Max(viewport.MinX, domainMinX ?? viewport.MinX);
            double maxX = Math.Min(viewport.MaxX, domainMaxX ?? viewport.MaxX);
            double minY = Math.Max(viewport.MinY, domainMinY ?? viewport.MinY);
            double maxY = Math.Min(viewport.MaxY, domainMaxY ?? viewport.MaxY);
            double minZ = viewport.MinZ;
            double maxZ = viewport.MaxZ;
            var result = new ImplicitSurfaceSample();
            if (!(minX < maxX) || !(minY < maxY) || !(minZ < maxZ)) return result;

            int size = resolution + 1;
            var field = new double[size, size, size];
            var localVariables = new Dictionary<string, double>(variables, StringComparer.OrdinalIgnoreCase);

            for (int iz = 0; iz <= resolution; iz++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double z = minZ + (maxZ - minZ) * iz / resolution;
                localVariables["z"] = z;
                for (int iy = 0; iy <= resolution; iy++)
                {
                    double y = minY + (maxY - minY) * iy / resolution;
                    for (int ix = 0; ix <= resolution; ix++)
                    {
                        double x = minX + (maxX - minX) * ix / resolution;
                        field[ix, iy, iz] = Evaluate2D(expression, x, y, localVariables);
                    }
                }
            }

            for (int iz = 0; iz < resolution; iz++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double z0 = minZ + (maxZ - minZ) * iz / resolution;
                double z1 = minZ + (maxZ - minZ) * (iz + 1) / resolution;
                for (int iy = 0; iy < resolution; iy++)
                {
                    double y0 = minY + (maxY - minY) * iy / resolution;
                    double y1 = minY + (maxY - minY) * (iy + 1) / resolution;
                    for (int ix = 0; ix < resolution; ix++)
                    {
                        double x0 = minX + (maxX - minX) * ix / resolution;
                        double x1 = minX + (maxX - minX) * (ix + 1) / resolution;

                        GraphPoint3D[] p =
                        {
                            new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0),
                            new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1)
                        };
                        double[] v =
                        {
                            field[ix, iy, iz], field[ix + 1, iy, iz], field[ix + 1, iy + 1, iz], field[ix, iy + 1, iz],
                            field[ix, iy, iz + 1], field[ix + 1, iy, iz + 1], field[ix + 1, iy + 1, iz + 1], field[ix, iy + 1, iz + 1]
                        };

                        foreach (int[] tet in CubeTetrahedra)
                        {
                            PolygoniseTetra(result, p, v, tet);
                        }
                    }
                }
            }

            return result;
        }

        private static void PolygoniseTetra(ImplicitSurfaceSample result, GraphPoint3D[] points, double[] values, int[] tet)
        {
            Span<int> inside = stackalloc int[4];
            Span<int> outside = stackalloc int[4];
            int ni = 0, no = 0;
            for (int i = 0; i < 4; i++)
            {
                int index = tet[i];
                double value = values[index];
                if (!double.IsFinite(value)) return;
                if (value < 0) inside[ni++] = index;
                else outside[no++] = index;
            }

            if (ni == 0 || ni == 4) return;

            GraphPoint3D insideCenter = Average(points, inside[..ni]);
            GraphPoint3D outsideCenter = Average(points, outside[..no]);

            if (ni == 1)
            {
                GraphPoint3D a = Interpolate(points[inside[0]], points[outside[0]], values[inside[0]], values[outside[0]]);
                GraphPoint3D b = Interpolate(points[inside[0]], points[outside[1]], values[inside[0]], values[outside[1]]);
                GraphPoint3D c = Interpolate(points[inside[0]], points[outside[2]], values[inside[0]], values[outside[2]]);
                AddTriangle(result, a, b, c, insideCenter, outsideCenter);
                return;
            }

            if (ni == 3)
            {
                GraphPoint3D a = Interpolate(points[outside[0]], points[inside[0]], values[outside[0]], values[inside[0]]);
                GraphPoint3D b = Interpolate(points[outside[0]], points[inside[2]], values[outside[0]], values[inside[2]]);
                GraphPoint3D c = Interpolate(points[outside[0]], points[inside[1]], values[outside[0]], values[inside[1]]);
                AddTriangle(result, a, b, c, insideCenter, outsideCenter);
                return;
            }

            GraphPoint3D p0 = Interpolate(points[inside[0]], points[outside[0]], values[inside[0]], values[outside[0]]);
            GraphPoint3D p1 = Interpolate(points[inside[0]], points[outside[1]], values[inside[0]], values[outside[1]]);
            GraphPoint3D p2 = Interpolate(points[inside[1]], points[outside[0]], values[inside[1]], values[outside[0]]);
            GraphPoint3D p3 = Interpolate(points[inside[1]], points[outside[1]], values[inside[1]], values[outside[1]]);
            AddTriangle(result, p0, p1, p2, insideCenter, outsideCenter);
            AddTriangle(result, p1, p3, p2, insideCenter, outsideCenter);
        }

        private static void AddTriangle(
            ImplicitSurfaceSample result,
            GraphPoint3D a,
            GraphPoint3D b,
            GraphPoint3D c,
            GraphPoint3D insideCenter,
            GraphPoint3D outsideCenter)
        {
            if (!Finite(a) || !Finite(b) || !Finite(c)) return;

            double abx = b.X - a.X, aby = b.Y - a.Y, abz = b.Z - a.Z;
            double acx = c.X - a.X, acy = c.Y - a.Y, acz = c.Z - a.Z;
            double nx = aby * acz - abz * acy;
            double ny = abz * acx - abx * acz;
            double nz = abx * acy - aby * acx;
            double dx = outsideCenter.X - insideCenter.X;
            double dy = outsideCenter.Y - insideCenter.Y;
            double dz = outsideCenter.Z - insideCenter.Z;
            if (nx * dx + ny * dy + nz * dz < 0) (b, c) = (c, b);

            int start = result.Vertices.Count;
            result.Vertices.Add(a);
            result.Vertices.Add(b);
            result.Vertices.Add(c);
            result.TriangleIndices.Add(start);
            result.TriangleIndices.Add(start + 1);
            result.TriangleIndices.Add(start + 2);
        }

        private static GraphPoint3D Average(GraphPoint3D[] points, ReadOnlySpan<int> indices)
        {
            double x = 0, y = 0, z = 0;
            foreach (int index in indices)
            {
                x += points[index].X;
                y += points[index].Y;
                z += points[index].Z;
            }
            double n = Math.Max(1, indices.Length);
            return new GraphPoint3D(x / n, y / n, z / n);
        }

        private static bool Crosses(double a, double b)
        {
            if (!double.IsFinite(a) || !double.IsFinite(b)) return false;
            if (Math.Abs(a) < 1e-14 || Math.Abs(b) < 1e-14) return true;
            return (a < 0) != (b < 0);
        }

        private static GraphPoint Interpolate(GraphPoint a, GraphPoint b, double va, double vb)
        {
            double denominator = va - vb;
            double t = Math.Abs(denominator) < 1e-15 ? 0.5 : Math.Clamp(va / denominator, 0.0, 1.0);
            return new GraphPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private static GraphPoint3D Interpolate(GraphPoint3D a, GraphPoint3D b, double va, double vb)
        {
            double denominator = va - vb;
            double t = Math.Abs(denominator) < 1e-15 ? 0.5 : Math.Clamp(va / denominator, 0.0, 1.0);
            return new GraphPoint3D(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t);
        }

        private static double Evaluate2D(
            CalculatorEngine.CompiledExpression expression,
            double x,
            double y,
            IDictionary<string, double> variables)
        {
            try
            {
                double value = expression.Evaluate(x, y, variables);
                return double.IsFinite(value) ? value : double.NaN;
            }
            catch
            {
                return double.NaN;
            }
        }

        private static bool Finite(GraphPoint3D p) =>
            double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    }
}
