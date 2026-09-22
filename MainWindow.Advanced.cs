using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private double _timelineStart;
        private double _timelineEnd = 10;
        private double _timelineTime;
        private double _timelineSpeed = 1;
        private bool _timelineLoop = true;
        private bool _timelinePlaying;
        private bool _updatingTimelineUi;
        private SurfaceDisplayMode _surfaceDisplayMode = SurfaceDisplayMode.Solid;

        private void InitializeAdvancedUi()
        {
            UpdateTimelineUi();
            if (Expressions.Count > 0)
            {
                ComparisonAComboBox.SelectedIndex = 0;
                CrossSectionExpressionComboBox.SelectedIndex = 0;
            }
            if (Expressions.Count > 1) ComparisonBComboBox.SelectedIndex = 1;
        }

        private void TimelinePlayButton_Click(object sender, RoutedEventArgs e)
        {
            ReadTimelineSettings();
            if (_timelineTime >= _timelineEnd - 1e-12) _timelineTime = _timelineStart;
            _timelinePlaying = true;
            UpdateTimelineUi();
            UpdateAnimationTimerState();
        }

        private void TimelinePauseButton_Click(object sender, RoutedEventArgs e)
        {
            _timelinePlaying = false;
            UpdateAnimationTimerState();
        }

        private void TimelineStopButton_Click(object sender, RoutedEventArgs e)
        {
            _timelinePlaying = false;
            _timelineTime = _timelineStart;
            UpdateTimelineUi();
            RefreshAllExpressionStatuses();
            ScheduleRender(0);
            UpdateAnimationTimerState();
        }

        private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingTimelineUi || TimelineSlider == null) return;
            _timelineTime = Math.Clamp(TimelineSlider.Value, _timelineStart, _timelineEnd);
            UpdateTimelineUi(updateSlider: false);
            RefreshAllExpressionStatuses();
            ScheduleRender(5);
        }

        private void TimelineSetting_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            ReadTimelineSettings();
            RefreshAllExpressionStatuses();
            ScheduleRender(10);
        }

        private void TimelineLoopCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingTimelineUi || TimelineLoopCheckBox == null) return;
            _timelineLoop = TimelineLoopCheckBox.IsChecked == true;
        }

        private void ReadTimelineSettings()
        {
            if (TimelineStartTextBox == null) return;

            Dictionary<string, double> values = Parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            double start = _timelineStart;
            double end = _timelineEnd;
            double speed = _timelineSpeed;
            double time = _timelineTime;

            TryEvaluateScalar(TimelineStartTextBox.Text, values, out start, out _);
            TryEvaluateScalar(TimelineEndTextBox.Text, values, out end, out _);
            TryEvaluateScalar(TimelineSpeedTextBox.Text, values, out speed, out _);
            TryEvaluateScalar(TimelineTimeTextBox.Text, values, out time, out _);

            if (!double.IsFinite(start) || !double.IsFinite(end) || start >= end)
            {
                start = _timelineStart;
                end = _timelineEnd;
            }

            _timelineStart = start;
            _timelineEnd = end;
            _timelineSpeed = double.IsFinite(speed) && speed > 0 ? speed : 1;
            _timelineTime = Math.Clamp(double.IsFinite(time) ? time : start, start, end);
            _timelineLoop = TimelineLoopCheckBox.IsChecked == true;
            UpdateTimelineUi();
        }

        private bool AdvanceTimeline(double deltaSeconds)
        {
            if (!_timelinePlaying || deltaSeconds <= 0 || !double.IsFinite(deltaSeconds)) return false;

            double previous = _timelineTime;
            _timelineTime += _timelineSpeed * deltaSeconds;
            if (_timelineTime > _timelineEnd)
            {
                if (_timelineLoop)
                {
                    double span = _timelineEnd - _timelineStart;
                    _timelineTime = span > 0
                        ? _timelineStart + (_timelineTime - _timelineStart) % span
                        : _timelineStart;
                }
                else
                {
                    _timelineTime = _timelineEnd;
                    _timelinePlaying = false;
                }
            }

            UpdateTimelineUi();
            return Math.Abs(_timelineTime - previous) > 1e-12;
        }

        private void UpdateTimelineUi(bool updateSlider = true)
        {
            if (TimelineSlider == null) return;

            _updatingTimelineUi = true;
            try
            {
                TimelineSlider.Minimum = _timelineStart;
                TimelineSlider.Maximum = _timelineEnd;
                if (updateSlider) TimelineSlider.Value = Math.Clamp(_timelineTime, _timelineStart, _timelineEnd);
                TimelineStartTextBox.Text = NumberFormatting.FormatAxis(_timelineStart);
                TimelineEndTextBox.Text = NumberFormatting.FormatAxis(_timelineEnd);
                TimelineTimeTextBox.Text = NumberFormatting.Format(_timelineTime);
                TimelineSpeedTextBox.Text = NumberFormatting.Format(_timelineSpeed);
                TimelineLoopCheckBox.IsChecked = _timelineLoop;
            }
            finally
            {
                _updatingTimelineUi = false;
            }

            if (DynamicsInspectorPanel?.Visibility == Visibility.Visible)
            {
                UpdateDynamicsCurrentStateText();
                if (DynamicsInspectorTabs?.SelectedIndex == 0) RedrawDynamicsTimeSeries();
            }
        }

        private void AnalysisOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace) return;
            ScheduleRender(20);
        }

        private void SurfaceDisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SurfaceDisplayModeComboBox == null) return;
            _surfaceDisplayMode = SurfaceDisplayModeComboBox.SelectedIndex switch
            {
                1 => SurfaceDisplayMode.Wireframe,
                2 => SurfaceDisplayMode.SolidWireframe,
                3 => SurfaceDisplayMode.HeightBands,
                4 => SurfaceDisplayMode.Slope,
                5 => SurfaceDisplayMode.Normals,
                _ => SurfaceDisplayMode.Solid
            };

            if (IsLoaded && !_loadingWorkspace) ScheduleRender(20);
        }

        private void CrossSectionOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace) return;
            ScheduleRender(20);
        }

        private void CrossSectionValueTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace) return;
            ScheduleRender(10);
        }

        private void DrawDerivativeOverlays(IDictionary<string, double> variables, double width, double height)
        {
            if (ShowDerivativeCheckBox?.IsChecked != true) return;

            foreach (GraphExpression item in Expressions.Where(item => item.IsVisible && item.Kind == GraphExpressionKind.Scalar))
            {
                CalculatorEngine.CompiledExpression? compiled = item.Compiled;
                if (compiled == null || compiled.DependsOnY) continue;
                if (!TryReadExpressionDomain(item, variables, out ExpressionDomain domain, out _, includeYOverride: false)) continue;

                double minX = Math.Max(_viewport.MinX, domain.MinX ?? _viewport.MinX);
                double maxX = Math.Min(_viewport.MaxX, domain.MaxX ?? _viewport.MaxX);
                if (!(minX < maxX)) continue;

                const int count = 520;
                double h = Math.Max((maxX - minX) / 9000.0, 1e-8);
                var points = new List<GraphPoint>(count);

                for (int i = 0; i < count; i++)
                {
                    double x = minX + (maxX - minX) * i / (count - 1.0);
                    try
                    {
                        double left = compiled.Evaluate(x - h, variables);
                        double right = compiled.Evaluate(x + h, variables);
                        double derivative = (right - left) / (2 * h);
                        points.Add(double.IsFinite(derivative) ? new GraphPoint(x, derivative) : new GraphPoint(x, double.NaN));
                    }
                    catch
                    {
                        points.Add(new GraphPoint(x, double.NaN));
                    }
                }

                DrawAnalysisSeries(points, item.Color, width, height, new DoubleCollection { 5, 3 }, 1.2, 0.72);
            }
        }

        private void DrawComparisonOverlay(IDictionary<string, double> variables, double width, double height)
        {
            if (ComparisonEnabledCheckBox?.IsChecked != true) return;
            if (ComparisonAComboBox?.SelectedItem is not GraphExpression a
                || ComparisonBComboBox?.SelectedItem is not GraphExpression b
                || a == b
                || a.Kind != GraphExpressionKind.Scalar
                || b.Kind != GraphExpressionKind.Scalar)
            {
                return;
            }

            CalculatorEngine.CompiledExpression? compiledA = a.Compiled;
            CalculatorEngine.CompiledExpression? compiledB = b.Compiled;
            if (compiledA == null || compiledB == null || compiledA.DependsOnY || compiledB.DependsOnY) return;

            if (!TryReadExpressionDomain(a, variables, out ExpressionDomain da, out _, includeYOverride: false)
                || !TryReadExpressionDomain(b, variables, out ExpressionDomain db, out _, includeYOverride: false)) return;

            double minX = Math.Max(_viewport.MinX, Math.Max(da.MinX ?? _viewport.MinX, db.MinX ?? _viewport.MinX));
            double maxX = Math.Min(_viewport.MaxX, Math.Min(da.MaxX ?? _viewport.MaxX, db.MaxX ?? _viewport.MaxX));
            if (!(minX < maxX)) return;

            string mode = (ComparisonModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A - B";
            const int count = 700;
            var points = new List<GraphPoint>(count);
            for (int i = 0; i < count; i++)
            {
                double x = minX + (maxX - minX) * i / (count - 1.0);
                try
                {
                    double va = compiledA.Evaluate(x, variables);
                    double vb = compiledB.Evaluate(x, variables);
                    double result = mode switch
                    {
                        "|A - B|" => Math.Abs(va - vb),
                        "A / B" => Math.Abs(vb) < 1e-12 ? double.NaN : va / vb,
                        _ => va - vb
                    };
                    points.Add(new GraphPoint(x, result));
                }
                catch
                {
                    points.Add(new GraphPoint(x, double.NaN));
                }
            }

            DrawAnalysisSeries(points, ThemeBrush("TextBrush", Brushes.Black), width, height, new DoubleCollection { 2, 2 }, 2.0, 0.88);
        }

        private void DrawAnalysisSeries(
            IReadOnlyList<GraphPoint> points,
            Brush source,
            double width,
            double height,
            DoubleCollection dash,
            double thickness,
            double opacity)
        {
            Polyline? line = null;
            foreach (GraphPoint point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                {
                    line = null;
                    continue;
                }

                double px = TransformX(point.X, _viewport, width);
                double py = TransformY(point.Y, _viewport, height);
                if (line == null)
                {
                    Brush brush = source.CloneCurrentValue();
                    brush.Opacity = opacity;
                    line = new Polyline
                    {
                        Stroke = brush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dash,
                        StrokeLineJoin = PenLineJoin.Round,
                        Points = new PointCollection()
                    };
                    PlotCanvas.Children.Add(line);
                }
                line.Points.Add(new Point(px, py));
            }
        }

        private void DrawVectorField(SeriesRequest request, IDictionary<string, double> variables, double width, double height)
        {
            if (request.Kind != GraphExpressionKind.VectorField2D) return;

            int columns = Math.Clamp((int)(width / 62), 7, 22);
            int rows = Math.Clamp((int)(height / 62), 6, 18);
            List<VectorFieldPoint> points = VectorFieldSampler.Sample(
                request.Components, _viewport, variables,
                request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                columns, rows, default);

            foreach (VectorFieldPoint point in points)
            {
                double sx = point.VX * width / _viewport.Width;
                double sy = -point.VY * height / _viewport.Height;
                double screenMagnitude = Math.Sqrt(sx * sx + sy * sy);
                if (!double.IsFinite(screenMagnitude) || screenMagnitude < 1e-10) continue;

                double dataMagnitude = point.Magnitude;
                double length = 9 + 15 * (1 - Math.Exp(-Math.Min(dataMagnitude, 50) * 0.3));
                double ux = sx / screenMagnitude;
                double uy = sy / screenMagnitude;
                double cx = TransformX(point.X, _viewport, width);
                double cy = TransformY(point.Y, _viewport, height);
                double ex = cx + ux * length;
                double ey = cy + uy * length;

                AddArrowLine(cx, cy, ex, ey, request.Expression.Color);
            }
        }

        private void AddArrowLine(double x0, double y0, double x1, double y1, Brush color)
        {
            PlotCanvas.Children.Add(new Line
            {
                X1 = x0,
                Y1 = y0,
                X2 = x1,
                Y2 = y1,
                Stroke = color,
                StrokeThickness = 1.35,
                Opacity = 0.82
            });

            double angle = Math.Atan2(y1 - y0, x1 - x0);
            const double head = 5.5;
            const double spread = 0.55;
            PlotCanvas.Children.Add(new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x1 - head * Math.Cos(angle - spread),
                Y2 = y1 - head * Math.Sin(angle - spread),
                Stroke = color,
                StrokeThickness = 1.35,
                Opacity = 0.82
            });
            PlotCanvas.Children.Add(new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x1 - head * Math.Cos(angle + spread),
                Y2 = y1 - head * Math.Sin(angle + spread),
                Stroke = color,
                StrokeThickness = 1.35,
                Opacity = 0.82
            });
        }

        private bool TryBuildCrossSection(
            IDictionary<string, double> variables,
            out List<GraphPoint3D> spatialPoints,
            out List<GraphPoint> plotPoints,
            out Brush color,
            out string title)
        {
            spatialPoints = [];
            plotPoints = [];
            color = ThemeBrush("TextBrush", Brushes.Black);
            title = "Cross-section";

            if (CrossSectionEnabledCheckBox?.IsChecked != true
                || CrossSectionExpressionComboBox?.SelectedItem is not GraphExpression item
                || item.Kind != GraphExpressionKind.Scalar)
            {
                return false;
            }

            CalculatorEngine.CompiledExpression? compiled = item.Compiled;
            if (compiled == null) return false;
            if (!TryReadExpressionDomain(item, variables, out ExpressionDomain domain, out _, includeYOverride: true)) return false;
            if (!TryEvaluateScalar(CrossSectionValueTextBox.Text, variables, out double fixedValue, out _)) return false;

            string axis = (CrossSectionAxisComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Y";
            bool fixedX = axis.Equals("X", StringComparison.OrdinalIgnoreCase);
            if (fixedX && ((domain.MinX.HasValue && fixedValue < domain.MinX.Value) || (domain.MaxX.HasValue && fixedValue > domain.MaxX.Value))) return false;
            if (!fixedX && ((domain.MinY.HasValue && fixedValue < domain.MinY.Value) || (domain.MaxY.HasValue && fixedValue > domain.MaxY.Value))) return false;

            double freeMin = fixedX ? Math.Max(_surfaceViewport.MinY, domain.MinY ?? _surfaceViewport.MinY) : Math.Max(_surfaceViewport.MinX, domain.MinX ?? _surfaceViewport.MinX);
            double freeMax = fixedX ? Math.Min(_surfaceViewport.MaxY, domain.MaxY ?? _surfaceViewport.MaxY) : Math.Min(_surfaceViewport.MaxX, domain.MaxX ?? _surfaceViewport.MaxX);
            if (!(freeMin < freeMax)) return false;

            // The slice is also drawn in 3D, so gaps have to survive as NaNs rather than being bridged.
            const int count = 320;
            for (int i = 0; i < count; i++)
            {
                double free = freeMin + (freeMax - freeMin) * i / (count - 1.0);
                double x = fixedX ? fixedValue : free;
                double y = fixedX ? free : fixedValue;
                try
                {
                    double z = compiled.Evaluate(x, y, variables);
                    if (!double.IsFinite(z))
                    {
                        spatialPoints.Add(new GraphPoint3D(double.NaN, double.NaN, double.NaN));
                        plotPoints.Add(new GraphPoint(free, double.NaN));
                    }
                    else
                    {
                        spatialPoints.Add(new GraphPoint3D(x, y, z));
                        plotPoints.Add(new GraphPoint(free, z));
                    }
                }
                catch
                {
                    spatialPoints.Add(new GraphPoint3D(double.NaN, double.NaN, double.NaN));
                    plotPoints.Add(new GraphPoint(free, double.NaN));
                }
            }

            color = item.Color;
            title = $"{axis} = {NumberFormatting.Format(fixedValue)} · {item.Expression}";
            return true;
        }

        private void DrawCrossSectionPreview(IReadOnlyList<GraphPoint> points, Brush color, string title)
        {
            if (CrossSectionCanvas == null || points.Count == 0)
            {
                CrossSectionPreview.Visibility = Visibility.Collapsed;
                return;
            }

            List<GraphPoint> finite = points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
            if (finite.Count < 2)
            {
                CrossSectionPreview.Visibility = Visibility.Collapsed;
                return;
            }

            double width = CrossSectionCanvas.ActualWidth > 20 ? CrossSectionCanvas.ActualWidth : 282;
            double height = CrossSectionCanvas.ActualHeight > 20 ? CrossSectionCanvas.ActualHeight : 145;
            double minX = finite.Min(p => p.X);
            double maxX = finite.Max(p => p.X);
            double minY = finite.Min(p => p.Y);
            double maxY = finite.Max(p => p.Y);
            if (Math.Abs(maxY - minY) < 1e-12) { minY -= 1; maxY += 1; }

            CrossSectionCanvas.Children.Clear();
            CrossSectionTitleText.Text = title;
            CrossSectionCanvas.Children.Add(new Line { X1 = 0, Y1 = height - 1, X2 = width, Y2 = height - 1, Stroke = Brushes.LightGray });
            CrossSectionCanvas.Children.Add(new Line { X1 = 1, Y1 = 0, X2 = 1, Y2 = height, Stroke = Brushes.LightGray });

            Polyline? line = null;
            foreach (GraphPoint point in points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) { line = null; continue; }
                if (line == null)
                {
                    line = new Polyline { Stroke = color, StrokeThickness = 1.6, Points = new PointCollection() };
                    CrossSectionCanvas.Children.Add(line);
                }
                double px = (point.X - minX) / (maxX - minX) * width;
                double py = height - (point.Y - minY) / (maxY - minY) * height;
                line.Points.Add(new Point(px, py));
            }

            CrossSectionPreview.Visibility = Visibility.Visible;
        }

        private void SurfaceViewport3D_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point point = e.GetPosition(SurfaceViewport3D);
            HitTestResult? hit = VisualTreeHelper.HitTest(SurfaceViewport3D, point);
            if (hit is not RayMeshGeometry3DHitTestResult ray)
            {
                SurfaceProbePanel.Visibility = Visibility.Collapsed;
                return;
            }

            GraphPoint3D dataPoint = SurfaceSceneBuilder.FromWorld(ray.PointHit, _surfaceViewport);
            var text = new StringBuilder();
            text.Append("x ").Append(NumberFormatting.Format(dataPoint.X))
                .Append("   y ").Append(NumberFormatting.Format(dataPoint.Y))
                .Append("   z ").Append(NumberFormatting.Format(dataPoint.Z));

            Dictionary<string, double> variables = GetParameterValues();
            GraphExpression? nearest = null;
            CalculatorEngine.CompiledExpression? nearestCompiled = null;
            double nearestDistance = double.PositiveInfinity;
            foreach (GraphExpression item in Expressions.Where(x => x.IsVisible && x.Kind == GraphExpressionKind.Scalar))
            {
                CalculatorEngine.CompiledExpression? compiled = item.Compiled;
                if (compiled == null) continue;
                try
                {
                    double z = compiled.Evaluate(dataPoint.X, dataPoint.Y, variables);
                    double distance = Math.Abs(z - dataPoint.Z);
                    if (double.IsFinite(z) && distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = item;
                        nearestCompiled = compiled;
                    }
                }
                catch { }
            }

            if (nearest != null && nearestCompiled != null && nearestDistance < Math.Max(_surfaceViewport.Height * 0.04, 1e-5))
            {
                double hx = Math.Max(_surfaceViewport.Width * 1e-4, 1e-6);
                double hy = Math.Max(_surfaceViewport.Depth * 1e-4, 1e-6);
                try
                {
                    double gx = (nearestCompiled.Evaluate(dataPoint.X + hx, dataPoint.Y, variables)
                        - nearestCompiled.Evaluate(dataPoint.X - hx, dataPoint.Y, variables)) / (2 * hx);
                    double gy = (nearestCompiled.Evaluate(dataPoint.X, dataPoint.Y + hy, variables)
                        - nearestCompiled.Evaluate(dataPoint.X, dataPoint.Y - hy, variables)) / (2 * hy);
                    double slope = Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI;
                    double nx = -gx;
                    double ny = -gy;
                    double nz = 1;
                    double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx /= length; ny /= length; nz /= length;

                    text.AppendLine()
                        .Append("surface: ").Append(nearest.Expression)
                        .AppendLine()
                        .Append("∂z/∂x ").Append(NumberFormatting.Format(gx))
                        .Append("   ∂z/∂y ").Append(NumberFormatting.Format(gy))
                        .Append("   slope ").Append(NumberFormatting.Format(slope)).Append("°")
                        .AppendLine()
                        .Append("normal (").Append(NumberFormatting.Format(nx)).Append(", ")
                        .Append(NumberFormatting.Format(ny)).Append(", ")
                        .Append(NumberFormatting.Format(nz)).Append(')');
                }
                catch { }
            }
            else
            {
                MeshGeometry3D? mesh = ray.MeshHit;
                if (mesh != null && ray.VertexIndex1 >= 0 && ray.VertexIndex2 >= 0 && ray.VertexIndex3 >= 0)
                {
                    Point3D a = mesh.Positions[ray.VertexIndex1];
                    Point3D b = mesh.Positions[ray.VertexIndex2];
                    Point3D c = mesh.Positions[ray.VertexIndex3];
                    Vector3D n = Vector3D.CrossProduct(b - a, c - a);
                    if (n.LengthSquared > 1e-12)
                    {
                        n.Normalize();
                        text.AppendLine().Append("mesh normal (")
                            .Append(NumberFormatting.Format(n.X)).Append(", ")
                            .Append(NumberFormatting.Format(n.Z)).Append(", ")
                            .Append(NumberFormatting.Format(n.Y)).Append(')');
                    }
                }
            }

            SurfaceProbeText.Text = text.ToString();
            SurfaceProbePanel.Visibility = Visibility.Visible;
            e.Handled = true;
        }

        private void ExportEngineButton_Click(object sender, RoutedEventArgs e)
        {
            GraphExpression? activeSource = _activeExpressionBox?.DataContext as GraphExpression;
            bool IsCurveSource(GraphExpression item) =>
                item.IsVisible && item.Kind == GraphExpressionKind.Scalar && item.Compiled != null && !item.Compiled.DependsOnY;
            bool IsHlslSource(GraphExpression item) =>
                item.IsVisible && item.CompiledParts.Any() && item.Kind is GraphExpressionKind.Scalar or GraphExpressionKind.TextureField2D or GraphExpressionKind.ComplexField2D or GraphExpressionKind.Implicit2D or GraphExpressionKind.Implicit3D;

            GraphExpression? curveSource = activeSource != null && IsCurveSource(activeSource)
                ? activeSource
                : Expressions.FirstOrDefault(IsCurveSource);
            GraphExpression? hlslSource = activeSource != null && IsHlslSource(activeSource)
                ? activeSource
                : Expressions.FirstOrDefault(IsHlslSource);

            if (curveSource == null && hlslSource == null)
            {
                GraphStatusText.Text = "Engine export needs a scalar, field or implicit expression";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export for an engine",
                Filter = "Unity AnimationCurve C# (*.cs)|*.cs|Godot Curve resource (*.tres)|*.tres|Unreal CurveTable CSV (*.csv)|*.csv|1D LUT PNG (*.png)|*.png|Engine samples JSON (*.json)|*.json|Curve samples CSV (*.csv)|*.csv|HLSL function (*.hlsl)|*.hlsl",
                FilterIndex = hlslSource != null && curveSource == null ? 7 : 1,
                AddExtension = true,
                FileName = "graph-export"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                if (dialog.FilterIndex == 7)
                {
                    if (hlslSource == null)
                    {
                        GraphStatusText.Text = "No compatible expression for HLSL export";
                        return;
                    }

                    File.WriteAllText(dialog.FileName, BuildHlslExport(hlslSource), new UTF8Encoding(false));
                    GraphStatusText.Text = "HLSL exported";
                    return;
                }

                if (curveSource == null)
                {
                    GraphStatusText.Text = "This export format needs a visible 1D scalar function";
                    return;
                }

                Dictionary<string, double> variables = GetParameterValues();
                if (!TryReadExpressionDomain(curveSource, variables, out ExpressionDomain domain, out _, includeYOverride: false)) return;
                double minX = Math.Max(_viewport.MinX, domain.MinX ?? _viewport.MinX);
                double maxX = Math.Min(_viewport.MaxX, domain.MaxX ?? _viewport.MaxX);
                if (!(minX < maxX)) return;

                List<GraphPoint> points = SampleScalarForExport(curveSource.Compiled!, variables, minX, maxX, 256);
                switch (dialog.FilterIndex)
                {
                    case 1:
                        File.WriteAllText(dialog.FileName, BuildUnityAnimationCurve(curveSource.Expression, points), new UTF8Encoding(false));
                        break;
                    case 2:
                        File.WriteAllText(dialog.FileName, BuildGodotCurveResource(points), new UTF8Encoding(false));
                        break;
                    case 3:
                        File.WriteAllText(dialog.FileName, BuildUnrealCurveTableCsv(points), new UTF8Encoding(false));
                        break;
                    case 4:
                        WriteLutPng(dialog.FileName, points);
                        break;
                    case 5:
                        WriteEngineJson(dialog.FileName, curveSource.Expression, variables, points);
                        break;
                    default:
                        WriteCurveCsv(dialog.FileName, points);
                        break;
                }
                GraphStatusText.Text = "Engine export written";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static List<GraphPoint> SampleScalarForExport(
            CalculatorEngine.CompiledExpression expression,
            IDictionary<string, double> variables,
            double minX,
            double maxX,
            int count)
        {
            var result = new List<GraphPoint>(count);
            for (int i = 0; i < count; i++)
            {
                double x = minX + (maxX - minX) * i / (count - 1.0);
                try
                {
                    double y = expression.Evaluate(x, variables);
                    result.Add(new GraphPoint(x, double.IsFinite(y) ? y : double.NaN));
                }
                catch
                {
                    result.Add(new GraphPoint(x, double.NaN));
                }
            }
            return result;
        }

        private static string BuildUnityAnimationCurve(string expression, IReadOnlyList<GraphPoint> points)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("public static class GraphCurveExport");
            sb.AppendLine("{");
            sb.AppendLine($"    // Source: {expression.Replace("\r", " ").Replace("\n", " ")}");
            sb.AppendLine("    public static AnimationCurve Create()");
            sb.AppendLine("    {");
            sb.AppendLine("        return new AnimationCurve(");
            List<GraphPoint> finite = points.Where(p => double.IsFinite(p.Y)).ToList();
            for (int i = 0; i < finite.Count; i++)
            {
                GraphPoint p = finite[i];
                string comma = i == finite.Count - 1 ? string.Empty : ",";
                sb.Append("            new Keyframe(")
                    .Append(((float)p.X).ToString("R", CultureInfo.InvariantCulture)).Append("f, ")
                    .Append(((float)p.Y).ToString("R", CultureInfo.InvariantCulture)).Append("f)")
                    .Append(comma).AppendLine();
            }
            sb.AppendLine("        );");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildGodotCurveResource(IReadOnlyList<GraphPoint> points)
        {
            List<GraphPoint> finite = points.Where(p => double.IsFinite(p.Y)).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("[gd_resource type=\"Curve\" format=3]");
            sb.AppendLine();
            sb.Append("[resource]\n_data = [");
            for (int i = 0; i < finite.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("Vector2(").Append(finite[i].X.ToString("R", CultureInfo.InvariantCulture)).Append(", ").Append(finite[i].Y.ToString("R", CultureInfo.InvariantCulture)).Append(')');
            }
            sb.AppendLine("]");
            return sb.ToString();
        }

        private static string BuildUnrealCurveTableCsv(IReadOnlyList<GraphPoint> points)
        {
            var sb = new StringBuilder("Name,Time,Value\n");
            foreach (GraphPoint p in points.Where(p => double.IsFinite(p.Y)))
                sb.Append("Curve,").Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            return sb.ToString();
        }

        private void WriteLutPng(string path, IReadOnlyList<GraphPoint> points)
        {
            const int width = 256;
            byte[] pixels = new byte[width * 4];
            for (int i = 0; i < width; i++)
            {
                GraphPoint p = points[Math.Min(i, points.Count - 1)];
                double normalized = double.IsFinite(p.Y)
                    ? Math.Clamp((p.Y - _viewport.MinY) / _viewport.Height, 0, 1)
                    : 0;
                byte value = (byte)Math.Round(normalized * 255);
                int offset = i * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }

            BitmapSource bitmap = BitmapSource.Create(width, 1, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void WriteEngineJson(
            string path,
            string expression,
            IDictionary<string, double> parameters,
            IReadOnlyList<GraphPoint> points)
        {
            var payload = new
            {
                expression,
                parameters,
                samples = points.Where(p => double.IsFinite(p.Y)).Select(p => new { x = p.X, y = p.Y }).ToArray()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        private static void WriteCurveCsv(string path, IReadOnlyList<GraphPoint> points)
        {
            var sb = new StringBuilder("Time,Value\n");
            foreach (GraphPoint p in points.Where(p => double.IsFinite(p.Y)))
            {
                sb.Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
