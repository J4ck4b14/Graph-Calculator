using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GraphCalculator
{
    public enum SurfaceDisplayMode
    {
        Solid,
        Wireframe,
        SolidWireframe,
        HeightBands,
        Slope,
        Normals
    }

    public sealed record SurfaceRenderLayer(SurfaceSample Sample, Brush Color);
    public sealed record ParametricSurfaceRenderLayer(ParametricSurfaceSample Sample, Brush Color);
    public sealed record ParametricCurveRenderLayer(IReadOnlyList<GraphPoint3D> Points, Brush Color, double Thickness = 0.028);
    public sealed record ImplicitSurfaceRenderLayer(ImplicitSurfaceSample Sample, Brush Color);

    public static class SurfaceSceneBuilder
    {
        private const double HalfSpan = 5.0;

        public static Model3DGroup Build(
            IReadOnlyList<SurfaceRenderLayer> layers,
            IReadOnlyList<ParametricSurfaceRenderLayer> parametricSurfaces,
            IReadOnlyList<ImplicitSurfaceRenderLayer> implicitSurfaces,
            IReadOnlyList<ParametricCurveRenderLayer> curves,
            SurfaceViewport viewport,
            SurfaceDisplayMode displayMode)
        {
            var scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Color.FromRgb(116, 122, 132)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(245, 247, 250), new Vector3D(-0.45, -0.9, -0.65)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(150, 158, 172), new Vector3D(0.55, 0.25, 0.6)));

            AddReferenceBox(scene, viewport);

            foreach (SurfaceRenderLayer layer in layers)
            {
                AddScalarSurface(scene, layer, viewport, displayMode);
            }

            foreach (ParametricSurfaceRenderLayer layer in parametricSurfaces)
            {
                AddParametricSurface(scene, layer, viewport, displayMode);
            }

            foreach (ImplicitSurfaceRenderLayer layer in implicitSurfaces)
            {
                AddImplicitSurface(scene, layer, viewport, displayMode);
            }

            foreach (ParametricCurveRenderLayer curve in curves)
            {
                MeshGeometry3D mesh = BuildCurveMesh(curve.Points, viewport, curve.Thickness);
                if (mesh.Positions.Count == 0) continue;

                Material material = CreateCurveMaterial(curve.Color);
                scene.Children.Add(new GeometryModel3D(mesh, material)
                {
                    BackMaterial = material
                });
            }

            return scene;
        }

        private static void AddScalarSurface(
            Model3DGroup scene,
            SurfaceRenderLayer layer,
            SurfaceViewport viewport,
            SurfaceDisplayMode displayMode)
        {
            if (displayMode is SurfaceDisplayMode.HeightBands or SurfaceDisplayMode.Slope)
            {
                AddScalarBands(scene, layer.Sample, viewport, displayMode);
                return;
            }

            if (displayMode != SurfaceDisplayMode.Wireframe)
            {
                MeshGeometry3D mesh = BuildSurfaceMesh(layer.Sample, viewport);
                AddMesh(scene, mesh, CreateSurfaceMaterial(layer.Color));
            }

            if (displayMode is SurfaceDisplayMode.Wireframe or SurfaceDisplayMode.SolidWireframe)
            {
                AddScalarWireframe(scene, layer.Sample, viewport, layer.Color);
            }

            if (displayMode == SurfaceDisplayMode.Normals)
            {
                AddScalarNormals(scene, layer.Sample, viewport);
            }
        }

        private static void AddParametricSurface(
            Model3DGroup scene,
            ParametricSurfaceRenderLayer layer,
            SurfaceViewport viewport,
            SurfaceDisplayMode displayMode)
        {
            if (displayMode is SurfaceDisplayMode.HeightBands or SurfaceDisplayMode.Slope)
            {
                AddParametricBands(scene, layer.Sample, viewport, displayMode);
                return;
            }

            if (displayMode != SurfaceDisplayMode.Wireframe)
            {
                MeshGeometry3D mesh = BuildParametricSurfaceMesh(layer.Sample, viewport);
                AddMesh(scene, mesh, CreateSurfaceMaterial(layer.Color));
            }

            if (displayMode is SurfaceDisplayMode.Wireframe or SurfaceDisplayMode.SolidWireframe)
            {
                AddParametricWireframe(scene, layer.Sample, viewport, layer.Color);
            }

            if (displayMode == SurfaceDisplayMode.Normals)
            {
                AddParametricNormals(scene, layer.Sample, viewport);
            }
        }

        private static void AddImplicitSurface(
            Model3DGroup scene,
            ImplicitSurfaceRenderLayer layer,
            SurfaceViewport viewport,
            SurfaceDisplayMode displayMode)
        {
            MeshGeometry3D mesh = BuildImplicitSurfaceMesh(layer.Sample, viewport);
            if (displayMode != SurfaceDisplayMode.Wireframe)
            {
                AddMesh(scene, mesh, CreateSurfaceMaterial(layer.Color));
            }

            if (displayMode is SurfaceDisplayMode.Wireframe or SurfaceDisplayMode.SolidWireframe)
            {
                AddImplicitWireframe(scene, layer.Sample, viewport, layer.Color);
            }

            if (displayMode == SurfaceDisplayMode.Normals)
            {
                AddImplicitNormals(scene, layer.Sample, viewport);
            }
        }

        private static MeshGeometry3D BuildImplicitSurfaceMesh(ImplicitSurfaceSample sample, SurfaceViewport viewport)
        {
            var mesh = new MeshGeometry3D();
            foreach (GraphPoint3D point in sample.Vertices)
            {
                mesh.Positions.Add(ToWorld(point.X, point.Y, point.Z, viewport));
            }
            foreach (int index in sample.TriangleIndices) mesh.TriangleIndices.Add(index);

            // Duplicated triangle vertices cost a little memory but give predictable normals at sharp SDF seams.
            for (int i = 0; i + 2 < mesh.Positions.Count; i += 3)
            {
                Vector3D normal = Vector3D.CrossProduct(mesh.Positions[i + 1] - mesh.Positions[i], mesh.Positions[i + 2] - mesh.Positions[i]);
                if (normal.LengthSquared > 1e-14) normal.Normalize();
                else normal = new Vector3D(0, 1, 0);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
            }

            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static void AddImplicitWireframe(Model3DGroup scene, ImplicitSurfaceSample sample, SurfaceViewport viewport, Brush color)
        {
            var edges = new List<GraphPoint3D>();
            int triangleCount = sample.Vertices.Count / 3;
            int stride = Math.Max(1, triangleCount / 2500);
            for (int triangle = 0; triangle < triangleCount; triangle += stride)
            {
                int i = triangle * 3;
                GraphPoint3D a = sample.Vertices[i];
                GraphPoint3D b = sample.Vertices[i + 1];
                GraphPoint3D c = sample.Vertices[i + 2];
                edges.Add(a); edges.Add(b); edges.Add(new GraphPoint3D(double.NaN, double.NaN, double.NaN));
                edges.Add(b); edges.Add(c); edges.Add(new GraphPoint3D(double.NaN, double.NaN, double.NaN));
                edges.Add(c); edges.Add(a); edges.Add(new GraphPoint3D(double.NaN, double.NaN, double.NaN));
            }

            MeshGeometry3D mesh = BuildCurveMesh(edges, viewport, 0.012);
            AddMesh(scene, mesh, CreateCurveMaterial(color));
        }

        private static void AddImplicitNormals(Model3DGroup scene, ImplicitSurfaceSample sample, SurfaceViewport viewport)
        {
            var lines = new List<GraphPoint3D>();
            int triangleCount = sample.Vertices.Count / 3;
            int stride = Math.Max(1, triangleCount / 180);
            double normalLength = Math.Max(viewport.MaxX - viewport.MinX, Math.Max(viewport.MaxY - viewport.MinY, viewport.MaxZ - viewport.MinZ)) * 0.025;

            for (int triangle = 0; triangle < triangleCount; triangle += stride)
            {
                int i = triangle * 3;
                GraphPoint3D a = sample.Vertices[i];
                GraphPoint3D b = sample.Vertices[i + 1];
                GraphPoint3D c = sample.Vertices[i + 2];
                var ab = new Vector3D(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                var ac = new Vector3D(c.X - a.X, c.Y - a.Y, c.Z - a.Z);
                Vector3D n = Vector3D.CrossProduct(ab, ac);
                if (n.LengthSquared < 1e-14) continue;
                n.Normalize();
                var center = new GraphPoint3D((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, (a.Z + b.Z + c.Z) / 3.0);
                var tip = new GraphPoint3D(center.X + n.X * normalLength, center.Y + n.Y * normalLength, center.Z + n.Z * normalLength);
                lines.Add(center); lines.Add(tip); lines.Add(new GraphPoint3D(double.NaN, double.NaN, double.NaN));
            }

            MeshGeometry3D mesh = BuildCurveMesh(lines, viewport, 0.01);
            AddMesh(scene, mesh, CreateCurveMaterial(Brushes.DimGray));
        }

        private static void AddMesh(Model3DGroup scene, MeshGeometry3D mesh, Material material)
        {
            if (mesh.Positions.Count == 0) return;
            scene.Children.Add(new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            });
        }

        private static MeshGeometry3D BuildCurveMesh(
            IReadOnlyList<GraphPoint3D> points,
            SurfaceViewport viewport,
            double thickness)
        {
            var mesh = new MeshGeometry3D();
            Point3D? previous = null;
            double radius = Math.Clamp(thickness, 0.008, 0.12);

            foreach (GraphPoint3D point in points)
            {
                if (!IsVisible(point, viewport))
                {
                    previous = null;
                    continue;
                }

                Point3D current = ToWorld(point.X, point.Y, point.Z, viewport);
                if (previous.HasValue)
                {
                    AppendCylinder(mesh, previous.Value, current, radius, 6);
                }

                previous = current;
            }

            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static void AppendCylinder(MeshGeometry3D mesh, Point3D start, Point3D end, double radius, int sides)
        {
            Vector3D axis = end - start;
            if (axis.LengthSquared < 1e-14) return;
            axis.Normalize();

            Vector3D helper = Math.Abs(Vector3D.DotProduct(axis, new Vector3D(0, 1, 0))) > 0.9
                ? new Vector3D(1, 0, 0)
                : new Vector3D(0, 1, 0);

            Vector3D u = Vector3D.CrossProduct(axis, helper);
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct(axis, u);
            v.Normalize();

            int startIndex = mesh.Positions.Count;
            for (int i = 0; i < sides; i++)
            {
                double angle = Math.PI * 2.0 * i / sides;
                Vector3D offset = (Math.Cos(angle) * u + Math.Sin(angle) * v) * radius;
                Vector3D normal = offset;
                normal.Normalize();

                mesh.Positions.Add(start + offset);
                mesh.Positions.Add(end + offset);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
            }

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int a = startIndex + i * 2;
                int b = a + 1;
                int c = startIndex + next * 2;
                int d = c + 1;

                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(d);
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(d);
                mesh.TriangleIndices.Add(c);
            }
        }

        private static Material CreateCurveMaterial(Brush source)
        {
            Brush brush = source.CloneCurrentValue();
            brush.Opacity = 0.96;
            if (brush.CanFreeze) brush.Freeze();

            var material = new DiffuseMaterial(brush);
            if (material.CanFreeze) material.Freeze();
            return material;
        }

        // Scalar surfaces need the boundary samples; otherwise a circular domain turns into a saw blade.
        private static MeshGeometry3D BuildSurfaceMesh(SurfaceSample sample, SurfaceViewport viewport)
        {
            var mesh = new MeshGeometry3D();

            for (int row = 0; row < sample.Rows - 1; row++)
            {
                double y0 = viewport.MinY + viewport.Depth * row / (sample.Rows - 1);
                double y1 = viewport.MinY + viewport.Depth * (row + 1) / (sample.Rows - 1);

                for (int column = 0; column < sample.Columns - 1; column++)
                {
                    double x0 = viewport.MinX + viewport.Width * column / (sample.Columns - 1);
                    double x1 = viewport.MinX + viewport.Width * (column + 1) / (sample.Columns - 1);

                    var corners = new[]
                    {
                        new SurfaceVertex(x0, y0, sample[column, row]),
                        new SurfaceVertex(x0, y1, sample[column, row + 1]),
                        new SurfaceVertex(x1, y1, sample[column + 1, row + 1]),
                        new SurfaceVertex(x1, y0, sample[column + 1, row])
                    };

                    bool[] visible =
                    {
                        IsVisible(corners[0].Z, viewport),
                        IsVisible(corners[1].Z, viewport),
                        IsVisible(corners[2].Z, viewport),
                        IsVisible(corners[3].Z, viewport)
                    };

                    int visibleCount = 0;
                    for (int i = 0; i < visible.Length; i++)
                    {
                        if (visible[i]) visibleCount++;
                    }

                    if (visibleCount == 0) continue;

                    SurfaceBoundaryPoint?[] boundaries =
                    {
                        sample.GetVerticalBoundary(column, row),
                        sample.GetHorizontalBoundary(column, row + 1),
                        sample.GetVerticalBoundary(column + 1, row),
                        sample.GetHorizontalBoundary(column, row)
                    };

                    if (visibleCount == 2 && visible[0] == visible[2] && visible[1] == visible[3])
                    {
                        AddDiagonalPieces(mesh, corners, visible, boundaries, viewport);
                        continue;
                    }

                    var polygon = new List<Point3D>(6);
                    for (int i = 0; i < 4; i++)
                    {
                        int next = (i + 1) % 4;
                        if (visible[i]) polygon.Add(ToWorld(corners[i].X, corners[i].Y, corners[i].Z, viewport));

                        if (visible[i] != visible[next] && boundaries[i].HasValue)
                        {
                            SurfaceBoundaryPoint boundary = boundaries[i]!.Value;
                            polygon.Add(ToWorld(boundary.X, boundary.Y, boundary.Z, viewport));
                        }
                    }

                    AddPolygon(mesh, polygon);
                }
            }

            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static MeshGeometry3D BuildParametricSurfaceMesh(ParametricSurfaceSample sample, SurfaceViewport viewport)
        {
            var mesh = new MeshGeometry3D();
            if (sample.Columns < 2 || sample.Rows < 2) return mesh;

            for (int row = 0; row < sample.Rows - 1; row++)
            {
                for (int column = 0; column < sample.Columns - 1; column++)
                {
                    GraphPoint3D a = sample[column, row];
                    GraphPoint3D b = sample[column, row + 1];
                    GraphPoint3D c = sample[column + 1, row + 1];
                    GraphPoint3D d = sample[column + 1, row];

                    if (IsVisible(a, viewport) && IsVisible(b, viewport) && IsVisible(c, viewport))
                    {
                        AddTriangle(mesh, ToWorld(a, viewport), ToWorld(b, viewport), ToWorld(c, viewport), forceUp: false);
                    }
                    if (IsVisible(a, viewport) && IsVisible(c, viewport) && IsVisible(d, viewport))
                    {
                        AddTriangle(mesh, ToWorld(a, viewport), ToWorld(c, viewport), ToWorld(d, viewport), forceUp: false);
                    }
                }
            }

            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static void AddDiagonalPieces(
            MeshGeometry3D mesh,
            SurfaceVertex[] corners,
            bool[] visible,
            SurfaceBoundaryPoint?[] boundaries,
            SurfaceViewport viewport)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!visible[i]) continue;
                int previousEdge = (i + 3) % 4;
                int nextEdge = i;
                if (!boundaries[previousEdge].HasValue || !boundaries[nextEdge].HasValue) continue;

                SurfaceBoundaryPoint previous = boundaries[previousEdge]!.Value;
                SurfaceBoundaryPoint next = boundaries[nextEdge]!.Value;
                AddTriangle(
                    mesh,
                    ToWorld(corners[i].X, corners[i].Y, corners[i].Z, viewport),
                    ToWorld(next.X, next.Y, next.Z, viewport),
                    ToWorld(previous.X, previous.Y, previous.Z, viewport));
            }
        }

        private static void AddPolygon(MeshGeometry3D mesh, IReadOnlyList<Point3D> polygon)
        {
            if (polygon.Count < 3) return;
            Point3D origin = polygon[0];
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                AddTriangle(mesh, origin, polygon[i], polygon[i + 1]);
            }
        }

        private readonly record struct SurfaceVertex(double X, double Y, double Z);

        private static void AddTriangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, bool forceUp = true)
        {
            Vector3D ab = b - a;
            Vector3D ac = c - a;
            Vector3D normal = Vector3D.CrossProduct(ab, ac);
            if (normal.LengthSquared < 1e-16) return;
            normal.Normalize();
            if (forceUp && normal.Y < 0) normal.Negate();

            int start = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.TriangleIndices.Add(start);
            mesh.TriangleIndices.Add(start + 1);
            mesh.TriangleIndices.Add(start + 2);
        }

        private static void AddScalarWireframe(Model3DGroup scene, SurfaceSample sample, SurfaceViewport viewport, Brush source)
        {
            Material material = CreateWireMaterial(source);
            int stride = Math.Max(1, sample.Columns / 22);
            var mesh = new MeshGeometry3D();

            for (int row = 0; row < sample.Rows; row += stride)
            {
                double y = viewport.MinY + viewport.Depth * row / (sample.Rows - 1.0);
                for (int column = 0; column < sample.Columns - 1; column++)
                {
                    double x0 = viewport.MinX + viewport.Width * column / (sample.Columns - 1.0);
                    double x1 = viewport.MinX + viewport.Width * (column + 1) / (sample.Columns - 1.0);
                    double z0 = sample[column, row];
                    double z1 = sample[column + 1, row];
                    bool a = IsVisible(z0, viewport);
                    bool b = IsVisible(z1, viewport);

                    if (a && b)
                    {
                        AppendCylinder(mesh, ToWorld(x0, y, z0, viewport), ToWorld(x1, y, z1, viewport), 0.009, 4);
                    }
                    else if (a != b && sample.GetHorizontalBoundary(column, row) is SurfaceBoundaryPoint boundary)
                    {
                        Point3D edge = ToWorld(boundary.X, boundary.Y, boundary.Z, viewport);
                        Point3D valid = a ? ToWorld(x0, y, z0, viewport) : ToWorld(x1, y, z1, viewport);
                        AppendCylinder(mesh, valid, edge, 0.009, 4);
                    }
                }
            }

            for (int column = 0; column < sample.Columns; column += stride)
            {
                double x = viewport.MinX + viewport.Width * column / (sample.Columns - 1.0);
                for (int row = 0; row < sample.Rows - 1; row++)
                {
                    double y0 = viewport.MinY + viewport.Depth * row / (sample.Rows - 1.0);
                    double y1 = viewport.MinY + viewport.Depth * (row + 1) / (sample.Rows - 1.0);
                    double z0 = sample[column, row];
                    double z1 = sample[column, row + 1];
                    bool a = IsVisible(z0, viewport);
                    bool b = IsVisible(z1, viewport);

                    if (a && b)
                    {
                        AppendCylinder(mesh, ToWorld(x, y0, z0, viewport), ToWorld(x, y1, z1, viewport), 0.009, 4);
                    }
                    else if (a != b && sample.GetVerticalBoundary(column, row) is SurfaceBoundaryPoint boundary)
                    {
                        Point3D edge = ToWorld(boundary.X, boundary.Y, boundary.Z, viewport);
                        Point3D valid = a ? ToWorld(x, y0, z0, viewport) : ToWorld(x, y1, z1, viewport);
                        AppendCylinder(mesh, valid, edge, 0.009, 4);
                    }
                }
            }

            if (mesh.CanFreeze) mesh.Freeze();
            AddMesh(scene, mesh, material);
        }

        private static void AddParametricWireframe(Model3DGroup scene, ParametricSurfaceSample sample, SurfaceViewport viewport, Brush source)
        {
            Material material = CreateWireMaterial(source);
            int stride = Math.Max(1, sample.Columns / 22);
            var mesh = new MeshGeometry3D();

            for (int row = 0; row < sample.Rows; row += stride)
            {
                Point3D? previous = null;
                for (int column = 0; column < sample.Columns; column++)
                {
                    GraphPoint3D point = sample[column, row];
                    if (!IsVisible(point, viewport)) { previous = null; continue; }
                    Point3D current = ToWorld(point, viewport);
                    if (previous.HasValue) AppendCylinder(mesh, previous.Value, current, 0.009, 4);
                    previous = current;
                }
            }

            for (int column = 0; column < sample.Columns; column += stride)
            {
                Point3D? previous = null;
                for (int row = 0; row < sample.Rows; row++)
                {
                    GraphPoint3D point = sample[column, row];
                    if (!IsVisible(point, viewport)) { previous = null; continue; }
                    Point3D current = ToWorld(point, viewport);
                    if (previous.HasValue) AppendCylinder(mesh, previous.Value, current, 0.009, 4);
                    previous = current;
                }
            }

            if (mesh.CanFreeze) mesh.Freeze();
            AddMesh(scene, mesh, material);
        }

        private static void AddScalarBands(Model3DGroup scene, SurfaceSample sample, SurfaceViewport viewport, SurfaceDisplayMode mode)
        {
            MeshGeometry3D[] meshes = CreateBandMeshes();
            for (int row = 0; row < sample.Rows - 1; row++)
            {
                double y0 = viewport.MinY + viewport.Depth * row / (sample.Rows - 1.0);
                double y1 = viewport.MinY + viewport.Depth * (row + 1) / (sample.Rows - 1.0);
                for (int column = 0; column < sample.Columns - 1; column++)
                {
                    double x0 = viewport.MinX + viewport.Width * column / (sample.Columns - 1.0);
                    double x1 = viewport.MinX + viewport.Width * (column + 1) / (sample.Columns - 1.0);
                    var a = new GraphPoint3D(x0, y0, sample[column, row]);
                    var b = new GraphPoint3D(x0, y1, sample[column, row + 1]);
                    var c = new GraphPoint3D(x1, y1, sample[column + 1, row + 1]);
                    var d = new GraphPoint3D(x1, y0, sample[column + 1, row]);
                    AddBandTriangle(meshes, a, b, c, viewport, mode, forceUp: true);
                    AddBandTriangle(meshes, a, c, d, viewport, mode, forceUp: true);
                }
            }
            AddBandModels(scene, meshes);
        }

        private static void AddParametricBands(Model3DGroup scene, ParametricSurfaceSample sample, SurfaceViewport viewport, SurfaceDisplayMode mode)
        {
            MeshGeometry3D[] meshes = CreateBandMeshes();
            for (int row = 0; row < sample.Rows - 1; row++)
            {
                for (int column = 0; column < sample.Columns - 1; column++)
                {
                    GraphPoint3D a = sample[column, row];
                    GraphPoint3D b = sample[column, row + 1];
                    GraphPoint3D c = sample[column + 1, row + 1];
                    GraphPoint3D d = sample[column + 1, row];
                    AddBandTriangle(meshes, a, b, c, viewport, mode, forceUp: false);
                    AddBandTriangle(meshes, a, c, d, viewport, mode, forceUp: false);
                }
            }
            AddBandModels(scene, meshes);
        }

        private static MeshGeometry3D[] CreateBandMeshes()
        {
            var meshes = new MeshGeometry3D[7];
            for (int i = 0; i < meshes.Length; i++) meshes[i] = new MeshGeometry3D();
            return meshes;
        }

        private static void AddBandTriangle(
            MeshGeometry3D[] meshes,
            GraphPoint3D a,
            GraphPoint3D b,
            GraphPoint3D c,
            SurfaceViewport viewport,
            SurfaceDisplayMode mode,
            bool forceUp)
        {
            if (!IsVisible(a, viewport) || !IsVisible(b, viewport) || !IsVisible(c, viewport)) return;

            double metric;
            if (mode == SurfaceDisplayMode.HeightBands)
            {
                double z = (a.Z + b.Z + c.Z) / 3.0;
                metric = (z - viewport.MinZ) / viewport.Height;
            }
            else
            {
                Vector3D ab = new(b.X - a.X, b.Z - a.Z, b.Y - a.Y);
                Vector3D ac = new(c.X - a.X, c.Z - a.Z, c.Y - a.Y);
                Vector3D n = Vector3D.CrossProduct(ab, ac);
                if (n.LengthSquared < 1e-16) return;
                n.Normalize();
                metric = 1.0 - Math.Abs(n.Y);
            }

            int band = Math.Clamp((int)Math.Floor(Math.Clamp(metric, 0, 0.999999) * meshes.Length), 0, meshes.Length - 1);
            AddTriangle(meshes[band], ToWorld(a, viewport), ToWorld(b, viewport), ToWorld(c, viewport), forceUp);
        }

        private static void AddBandModels(Model3DGroup scene, MeshGeometry3D[] meshes)
        {
            Color[] colors =
            {
                Color.FromRgb(58, 94, 168),
                Color.FromRgb(58, 145, 184),
                Color.FromRgb(66, 164, 132),
                Color.FromRgb(135, 175, 86),
                Color.FromRgb(205, 175, 75),
                Color.FromRgb(219, 126, 66),
                Color.FromRgb(184, 73, 73)
            };

            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i].Positions.Count == 0) continue;
                if (meshes[i].CanFreeze) meshes[i].Freeze();
                Material material = CreateFlatMaterial(colors[i], 0.9);
                AddMesh(scene, meshes[i], material);
            }
        }

        private static void AddScalarNormals(Model3DGroup scene, SurfaceSample sample, SurfaceViewport viewport)
        {
            var normalMaterial = CreateFlatMaterial(Color.FromRgb(55, 61, 70), 0.8);
            int stride = Math.Max(3, sample.Columns / 12);

            for (int row = stride; row < sample.Rows - stride; row += stride)
            {
                for (int column = stride; column < sample.Columns - stride; column += stride)
                {
                    double z = sample[column, row];
                    double zx0 = sample[column - 1, row];
                    double zx1 = sample[column + 1, row];
                    double zy0 = sample[column, row - 1];
                    double zy1 = sample[column, row + 1];
                    if (!IsVisible(z, viewport) || !IsVisible(zx0, viewport) || !IsVisible(zx1, viewport)
                        || !IsVisible(zy0, viewport) || !IsVisible(zy1, viewport)) continue;

                    double x = viewport.MinX + viewport.Width * column / (sample.Columns - 1.0);
                    double y = viewport.MinY + viewport.Depth * row / (sample.Rows - 1.0);
                    double dx = viewport.Width / (sample.Columns - 1.0);
                    double dy = viewport.Depth / (sample.Rows - 1.0);
                    Point3D center = ToWorld(x, y, z, viewport);
                    Point3D px0 = ToWorld(x - dx, y, zx0, viewport);
                    Point3D px1 = ToWorld(x + dx, y, zx1, viewport);
                    Point3D py0 = ToWorld(x, y - dy, zy0, viewport);
                    Point3D py1 = ToWorld(x, y + dy, zy1, viewport);
                    AddNormal(scene, center, px1 - px0, py1 - py0, normalMaterial, forceUp: true);
                }
            }
        }

        private static void AddParametricNormals(Model3DGroup scene, ParametricSurfaceSample sample, SurfaceViewport viewport)
        {
            var normalMaterial = CreateFlatMaterial(Color.FromRgb(55, 61, 70), 0.8);
            int stride = Math.Max(3, sample.Columns / 12);

            for (int row = stride; row < sample.Rows - stride; row += stride)
            {
                for (int column = stride; column < sample.Columns - stride; column += stride)
                {
                    GraphPoint3D p = sample[column, row];
                    GraphPoint3D left = sample[column - 1, row];
                    GraphPoint3D right = sample[column + 1, row];
                    GraphPoint3D down = sample[column, row - 1];
                    GraphPoint3D up = sample[column, row + 1];
                    if (!IsVisible(p, viewport) || !IsVisible(left, viewport) || !IsVisible(right, viewport)
                        || !IsVisible(down, viewport) || !IsVisible(up, viewport)) continue;

                    Point3D center = ToWorld(p, viewport);
                    Vector3D du = ToWorld(right, viewport) - ToWorld(left, viewport);
                    Vector3D dv = ToWorld(up, viewport) - ToWorld(down, viewport);
                    AddNormal(scene, center, du, dv, normalMaterial, forceUp: false);
                }
            }
        }

        private static void AddNormal(Model3DGroup scene, Point3D center, Vector3D tangentA, Vector3D tangentB, Material material, bool forceUp)
        {
            Vector3D normal = Vector3D.CrossProduct(tangentA, tangentB);
            if (normal.LengthSquared < 1e-12) return;
            normal.Normalize();
            if (forceUp && normal.Y < 0) normal.Negate();
            AddLine(scene, center, center + normal * 0.55, 0.012, material);
        }

        private static Material CreateWireMaterial(Brush source)
        {
            Color color = source is SolidColorBrush solid ? solid.Color : Color.FromRgb(55, 61, 70);
            color = Color.FromRgb(
                (byte)Math.Clamp(color.R * 0.55, 0, 255),
                (byte)Math.Clamp(color.G * 0.55, 0, 255),
                (byte)Math.Clamp(color.B * 0.55, 0, 255));
            return CreateFlatMaterial(color, 0.9);
        }

        private static bool IsVisible(double z, SurfaceViewport viewport)
        {
            return double.IsFinite(z) && z >= viewport.MinZ && z <= viewport.MaxZ;
        }

        private static bool IsVisible(GraphPoint3D point, SurfaceViewport viewport)
        {
            return ParametricSurfaceSampler.IsFinite(point)
                && point.X >= viewport.MinX && point.X <= viewport.MaxX
                && point.Y >= viewport.MinY && point.Y <= viewport.MaxY
                && point.Z >= viewport.MinZ && point.Z <= viewport.MaxZ;
        }

        private static Point3D ToWorld(GraphPoint3D point, SurfaceViewport viewport) =>
            ToWorld(point.X, point.Y, point.Z, viewport);

        private static Point3D ToWorld(double x, double y, double z, SurfaceViewport viewport)
        {
            return new Point3D(
                Map(x, viewport.MinX, viewport.MaxX),
                Map(z, viewport.MinZ, viewport.MaxZ),
                Map(y, viewport.MinY, viewport.MaxY));
        }

        public static GraphPoint3D FromWorld(Point3D point, SurfaceViewport viewport)
        {
            return new GraphPoint3D(
                Unmap(point.X, viewport.MinX, viewport.MaxX),
                Unmap(point.Z, viewport.MinY, viewport.MaxY),
                Unmap(point.Y, viewport.MinZ, viewport.MaxZ));
        }

        private static double Map(double value, double min, double max)
        {
            return -HalfSpan + (value - min) / (max - min) * HalfSpan * 2.0;
        }

        private static double Unmap(double value, double min, double max)
        {
            return min + (value + HalfSpan) / (HalfSpan * 2.0) * (max - min);
        }

        private static Material CreateSurfaceMaterial(Brush source)
        {
            Brush diffuseBrush = source.CloneCurrentValue();
            diffuseBrush.Opacity = 0.82;
            if (diffuseBrush.CanFreeze) diffuseBrush.Freeze();

            var highlightBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255)) { Opacity = 0.28 };
            highlightBrush.Freeze();

            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(diffuseBrush));
            group.Children.Add(new SpecularMaterial(highlightBrush, 28));
            if (group.CanFreeze) group.Freeze();
            return group;
        }

        private static void AddReferenceBox(Model3DGroup scene, SurfaceViewport viewport)
        {
            var gridMaterial = CreateFlatMaterial(Color.FromRgb(220, 224, 230), 0.78);
            var boxMaterial = CreateFlatMaterial(Color.FromRgb(184, 190, 200), 0.9);
            var xMaterial = CreateFlatMaterial(Color.FromRgb(190, 74, 74), 1.0);
            var yMaterial = CreateFlatMaterial(Color.FromRgb(67, 145, 98), 1.0);
            var zMaterial = CreateFlatMaterial(Color.FromRgb(73, 111, 178), 1.0);

            double floorY = viewport.MinZ <= 0 && viewport.MaxZ >= 0
                ? Map(0, viewport.MinZ, viewport.MaxZ)
                : -HalfSpan;

            const int gridDivisions = 10;
            for (int i = 0; i <= gridDivisions; i++)
            {
                double p = -HalfSpan + i * HalfSpan * 2.0 / gridDivisions;
                AddLine(scene, new Point3D(-HalfSpan, floorY, p), new Point3D(HalfSpan, floorY, p), 0.008, gridMaterial);
                AddLine(scene, new Point3D(p, floorY, -HalfSpan), new Point3D(p, floorY, HalfSpan), 0.008, gridMaterial);
            }

            Point3D[] corners =
            {
                new(-HalfSpan, -HalfSpan, -HalfSpan), new(HalfSpan, -HalfSpan, -HalfSpan),
                new(HalfSpan, HalfSpan, -HalfSpan), new(-HalfSpan, HalfSpan, -HalfSpan),
                new(-HalfSpan, -HalfSpan, HalfSpan), new(HalfSpan, -HalfSpan, HalfSpan),
                new(HalfSpan, HalfSpan, HalfSpan), new(-HalfSpan, HalfSpan, HalfSpan)
            };

            int[,] edges =
            {
                { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
                { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
            };

            for (int i = 0; i < edges.GetLength(0); i++)
            {
                AddLine(scene, corners[edges[i, 0]], corners[edges[i, 1]], 0.01, boxMaterial);
            }

            bool xOriginVisible = viewport.MinX <= 0 && viewport.MaxX >= 0;
            bool yOriginVisible = viewport.MinY <= 0 && viewport.MaxY >= 0;
            bool zOriginVisible = viewport.MinZ <= 0 && viewport.MaxZ >= 0;

            if (yOriginVisible && zOriginVisible)
            {
                double wy = Map(0, viewport.MinZ, viewport.MaxZ);
                double wz = Map(0, viewport.MinY, viewport.MaxY);
                AddLine(scene, new Point3D(-HalfSpan, wy, wz), new Point3D(HalfSpan, wy, wz), 0.025, xMaterial);
            }

            if (xOriginVisible && zOriginVisible)
            {
                double wx = Map(0, viewport.MinX, viewport.MaxX);
                double wy = Map(0, viewport.MinZ, viewport.MaxZ);
                AddLine(scene, new Point3D(wx, wy, -HalfSpan), new Point3D(wx, wy, HalfSpan), 0.025, yMaterial);
            }

            if (xOriginVisible && yOriginVisible)
            {
                double wx = Map(0, viewport.MinX, viewport.MaxX);
                double wz = Map(0, viewport.MinY, viewport.MaxY);
                AddLine(scene, new Point3D(wx, -HalfSpan, wz), new Point3D(wx, HalfSpan, wz), 0.025, zMaterial);
            }
        }

        private static Material CreateFlatMaterial(Color color, double opacity)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            material.Freeze();
            return material;
        }

        private static void AddLine(Model3DGroup scene, Point3D start, Point3D end, double radius, Material material)
        {
            MeshGeometry3D mesh = CreateCylinder(start, end, radius, 6);
            if (mesh.Positions.Count == 0) return;
            scene.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }

        private static MeshGeometry3D CreateCylinder(Point3D start, Point3D end, double radius, int sides)
        {
            var mesh = new MeshGeometry3D();
            Vector3D axis = end - start;
            if (axis.LengthSquared < 1e-16) return mesh;
            axis.Normalize();

            Vector3D helper = Math.Abs(Vector3D.DotProduct(axis, new Vector3D(0, 1, 0))) > 0.9
                ? new Vector3D(1, 0, 0)
                : new Vector3D(0, 1, 0);

            Vector3D u = Vector3D.CrossProduct(axis, helper);
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct(axis, u);
            v.Normalize();

            for (int i = 0; i < sides; i++)
            {
                double angle = Math.PI * 2.0 * i / sides;
                Vector3D offset = (Math.Cos(angle) * u + Math.Sin(angle) * v) * radius;
                Vector3D normal = offset;
                normal.Normalize();
                mesh.Positions.Add(start + offset);
                mesh.Positions.Add(end + offset);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
            }

            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int a = i * 2;
                int b = a + 1;
                int c = next * 2;
                int d = c + 1;
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(d);
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(d);
                mesh.TriangleIndices.Add(c);
            }

            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }
    }
}
