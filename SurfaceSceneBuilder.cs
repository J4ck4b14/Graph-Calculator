using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GraphCalculator
{
    public sealed record SurfaceRenderLayer(SurfaceSample Sample, Brush Color);

    public static class SurfaceSceneBuilder
    {
        private const double HalfSpan = 5.0;

        public static Model3DGroup Build(
            IReadOnlyList<SurfaceRenderLayer> layers,
            SurfaceViewport viewport)
        {
            var scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Color.FromRgb(116, 122, 132)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(245, 247, 250), new Vector3D(-0.45, -0.9, -0.65)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(150, 158, 172), new Vector3D(0.55, 0.25, 0.6)));

            AddReferenceBox(scene, viewport);

            foreach (SurfaceRenderLayer layer in layers)
            {
                MeshGeometry3D mesh = BuildSurfaceMesh(layer.Sample, viewport);
                if (mesh.Positions.Count == 0) continue;

                Material material = CreateSurfaceMaterial(layer.Color);
                var model = new GeometryModel3D(mesh, material)
                {
                    BackMaterial = material
                };

                scene.Children.Add(model);
            }

            return scene;
        }

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

                    double z00 = sample[column, row];
                    double z10 = sample[column + 1, row];
                    double z01 = sample[column, row + 1];
                    double z11 = sample[column + 1, row + 1];

                    bool v00 = IsVisible(z00, viewport);
                    bool v10 = IsVisible(z10, viewport);
                    bool v01 = IsVisible(z01, viewport);
                    bool v11 = IsVisible(z11, viewport);

                    if (v00 && v11 && v10)
                    {
                        AddTriangle(
                            mesh,
                            ToWorld(x0, y0, z00, viewport),
                            ToWorld(x1, y1, z11, viewport),
                            ToWorld(x1, y0, z10, viewport));
                    }

                    if (v00 && v01 && v11)
                    {
                        AddTriangle(
                            mesh,
                            ToWorld(x0, y0, z00, viewport),
                            ToWorld(x0, y1, z01, viewport),
                            ToWorld(x1, y1, z11, viewport));
                    }
                }
            }

            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static void AddTriangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
        {
            Vector3D ab = b - a;
            Vector3D ac = c - a;
            Vector3D normal = Vector3D.CrossProduct(ab, ac);

            if (normal.LengthSquared < 1e-16) return;
            normal.Normalize();

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

        private static bool IsVisible(double z, SurfaceViewport viewport)
        {
            return double.IsFinite(z) && z >= viewport.MinZ && z <= viewport.MaxZ;
        }

        private static Point3D ToWorld(double x, double y, double z, SurfaceViewport viewport)
        {
            return new Point3D(
                Map(x, viewport.MinX, viewport.MaxX),
                Map(z, viewport.MinZ, viewport.MaxZ),
                Map(y, viewport.MinY, viewport.MaxY));
        }

        private static double Map(double value, double min, double max)
        {
            return -HalfSpan + (value - min) / (max - min) * HalfSpan * 2.0;
        }

        private static Material CreateSurfaceMaterial(Brush source)
        {
            Brush diffuseBrush = source.CloneCurrentValue();
            diffuseBrush.Opacity = 0.82;
            if (diffuseBrush.CanFreeze) diffuseBrush.Freeze();

            var highlightBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255))
            {
                Opacity = 0.28
            };
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
                new(-HalfSpan, -HalfSpan, -HalfSpan),
                new(HalfSpan, -HalfSpan, -HalfSpan),
                new(HalfSpan, HalfSpan, -HalfSpan),
                new(-HalfSpan, HalfSpan, -HalfSpan),
                new(-HalfSpan, -HalfSpan, HalfSpan),
                new(HalfSpan, -HalfSpan, HalfSpan),
                new(HalfSpan, HalfSpan, HalfSpan),
                new(-HalfSpan, HalfSpan, HalfSpan)
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

        private static void AddLine(
            Model3DGroup scene,
            Point3D start,
            Point3D end,
            double radius,
            Material material)
        {
            MeshGeometry3D mesh = CreateCylinder(start, end, radius, 6);
            if (mesh.Positions.Count == 0) return;

            scene.Children.Add(new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            });
        }

        private static MeshGeometry3D CreateCylinder(
            Point3D start,
            Point3D end,
            double radius,
            int sides)
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
