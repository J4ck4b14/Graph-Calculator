using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private enum RenderQuality
        {
            Draft,
            Normal,
            High,
            Ultra
        }

        private RenderQuality _renderQuality = RenderQuality.Normal;
        private readonly Dictionary<string, double> _parameterStateA = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _parameterStateB = new(StringComparer.OrdinalIgnoreCase);
        private bool _applyingParameterState;

        private readonly Stack<EditSnapshot> _undoStack = new();
        private readonly Stack<EditSnapshot> _redoStack = new();
        private DispatcherTimer? _undoCaptureTimer;
        private EditSnapshot? _editBaseline;
        private EditSnapshot? _pendingUndoBefore;
        private bool _suppressUndo;
        private bool _productPassReady;

        private sealed record EditSnapshot(
            bool Is3D,
            PlotViewport PlotView,
            SurfaceViewport SurfaceView,
            double TimelineTime,
            List<WorkspaceExpression> Expressions,
            List<WorkspaceParameter> Parameters,
            List<WorkspaceCurveChannel> CurveChannels,
            Guid ActiveCurveChannelId,
            string CurveBeforeMode,
            string CurveAfterMode,
            double CurveAutoTension,
            List<WorkspaceSharedAsset> SharedAssets,
            List<WorkspaceImportedTable> ImportedTables,
            List<WorkspaceEconomyNode> EconomyNodes,
            List<WorkspaceEconomyLink> EconomyLinks,
            List<WorkspaceEconomyParameter> EconomyParameters,
            List<WorkspaceEconomyScenario> EconomyScenarios,
            List<WorkspaceEconomyCohort> EconomyCohorts,
            List<WorkspaceEconomyTarget> EconomyTargets,
            List<WorkspaceEconomySubsystem> EconomySubsystems,
            List<WorkspaceEconomyRecipe> EconomyRecipes,
            List<WorkspaceEconomyResourceStyle> EconomyResourceStyles);

        private void InitializeProductPass()
        {
            _undoCaptureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
            _undoCaptureTimer.Tick += (_, _) => CommitPendingUndoGroup();
            _editBaseline = CaptureEditSnapshot();
            _productPassReady = true;

            var parameterView = CollectionViewSource.GetDefaultView(Parameters);
            parameterView.GroupDescriptions.Clear();
            parameterView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GraphParameter.Group)));

            if (FieldPreviewExpressionComboBox != null && Expressions.Count > 0)
                FieldPreviewExpressionComboBox.SelectedIndex = 0;

            UpdateParameterStateBlendText();
        }

        private void RefreshParameterGroups()
        {
            CollectionViewSource.GetDefaultView(Parameters).Refresh();
        }

        private int GetLineSampleCount(int pixelWidth)
        {
            double scale = _renderQuality switch
            {
                RenderQuality.Draft => 0.55,
                RenderQuality.High => 1.55,
                RenderQuality.Ultra => 2.35,
                _ => 1.0
            };
            return Math.Clamp((int)Math.Round(pixelWidth * 2 * scale), 160, 4200);
        }

        private int GetParametricCurveSampleCount(bool threeDimensional)
        {
            int normal = threeDimensional ? 700 : 900;
            double scale = _renderQuality switch
            {
                RenderQuality.Draft => 0.5,
                RenderQuality.High => 1.5,
                RenderQuality.Ultra => 2.2,
                _ => 1.0
            };
            return Math.Clamp((int)Math.Round(normal * scale), 180, 3200);
        }

        private int GetSurfaceResolution(double width, double height)
        {
            int normal = Math.Clamp((int)(Math.Min(width, height) / 9.0), 34, 72);
            double scale = _renderQuality switch
            {
                RenderQuality.Draft => 0.62,
                RenderQuality.High => 1.28,
                RenderQuality.Ultra => 1.62,
                _ => 1.0
            };
            return Math.Clamp((int)Math.Round(normal * scale), 24, 112);
        }

        private int GetImplicit2DResolution() => _renderQuality switch
        {
            RenderQuality.Draft => 72,
            RenderQuality.High => 210,
            RenderQuality.Ultra => 320,
            _ => 132
        };

        private int GetImplicit3DResolution() => _renderQuality switch
        {
            RenderQuality.Draft => 15,
            RenderQuality.High => 30,
            RenderQuality.Ultra => 38,
            _ => 22
        };

        private int GetFieldPreviewResolution() => _renderQuality switch
        {
            RenderQuality.Draft => 112,
            RenderQuality.High => 300,
            RenderQuality.Ultra => 440,
            _ => 192
        };


        private bool TryFitDomainOnlyPlots(IReadOnlyList<SeriesRequest> active)
        {
            if (active.Count == 0 || active.Any(r => r.Kind is not (GraphExpressionKind.Implicit2D or GraphExpressionKind.Implicit3D or GraphExpressionKind.TextureField2D)))
                return false;

            if (_is3DMode)
            {
                double minX = active.Where(r => r.Domain.MinX.HasValue).Select(r => r.Domain.MinX!.Value).DefaultIfEmpty(_surfaceViewport.MinX).Min();
                double maxX = active.Where(r => r.Domain.MaxX.HasValue).Select(r => r.Domain.MaxX!.Value).DefaultIfEmpty(_surfaceViewport.MaxX).Max();
                double minY = active.Where(r => r.Domain.MinY.HasValue).Select(r => r.Domain.MinY!.Value).DefaultIfEmpty(_surfaceViewport.MinY).Min();
                double maxY = active.Where(r => r.Domain.MaxY.HasValue).Select(r => r.Domain.MaxY!.Value).DefaultIfEmpty(_surfaceViewport.MaxY).Max();
                if (minX < maxX && minY < maxY)
                {
                    _surfaceViewport = new SurfaceViewport(minX, maxX, minY, maxY, _surfaceViewport.MinZ, _surfaceViewport.MaxZ);
                    UpdateSurfaceRangeInputs();
                }
            }
            else
            {
                double minX = active.Where(r => r.Domain.MinX.HasValue).Select(r => r.Domain.MinX!.Value).DefaultIfEmpty(_viewport.MinX).Min();
                double maxX = active.Where(r => r.Domain.MaxX.HasValue).Select(r => r.Domain.MaxX!.Value).DefaultIfEmpty(_viewport.MaxX).Max();
                double minY = active.Where(r => r.Domain.MinY.HasValue).Select(r => r.Domain.MinY!.Value).DefaultIfEmpty(_viewport.MinY).Min();
                double maxY = active.Where(r => r.Domain.MaxY.HasValue).Select(r => r.Domain.MaxY!.Value).DefaultIfEmpty(_viewport.MaxY).Max();
                if (minX < maxX && minY < maxY)
                {
                    _viewport = new PlotViewport(minX, maxX, minY, maxY);
                    UpdatePlotRangeInputs();
                }
            }

            UpdateViewRangeText();
            GraphStatusText.Text = "Fit to explicit field/domain bounds";
            ScheduleRender(0);
            return true;
        }

        private void RenderQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace || RenderQualityComboBox == null) return;
            _renderQuality = RenderQualityComboBox.SelectedIndex switch
            {
                0 => RenderQuality.Draft,
                2 => RenderQuality.High,
                3 => RenderQuality.Ultra,
                _ => RenderQuality.Normal
            };
            ScheduleRender(0);
        }

        private void DrawImplicitContours(IReadOnlyList<GraphSegment> segments, Brush color, double width, double height)
        {
            foreach (GraphSegment segment in segments)
            {
                PlotCanvas.Children.Add(new Line
                {
                    X1 = TransformX(segment.A.X, _viewport, width),
                    Y1 = TransformY(segment.A.Y, _viewport, height),
                    X2 = TransformX(segment.B.X, _viewport, width),
                    Y2 = TransformY(segment.B.Y, _viewport, height),
                    Stroke = color,
                    StrokeThickness = 2.0,
                    SnapsToDevicePixels = true
                });
            }
        }

        private void FieldPreviewOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace) return;
            ScheduleRender(0);
        }

        private CalculatorEngine.CompiledExpression? GetFieldPreviewExpression(out GraphExpression? source)
        {
            source = FieldPreviewExpressionComboBox?.SelectedItem as GraphExpression;
            if (source == null || !source.IsVisible) return null;

            if (source.Kind == GraphExpressionKind.TextureField2D || source.Kind == GraphExpressionKind.Implicit2D)
                return source.Components.FirstOrDefault();
            return source.Kind == GraphExpressionKind.Scalar ? source.Compiled : null;
        }

        private void UpdateFieldPreview(IDictionary<string, double> variables)
        {
            bool explicitPreview = FieldPreviewEnabledCheckBox?.IsChecked == true;
            GraphExpression? automatic = Expressions.FirstOrDefault(e => e.IsVisible && e.Kind == GraphExpressionKind.TextureField2D && e.Components.Count > 0);
            if (!explicitPreview && automatic == null)
            {
                FieldPreviewImage.Visibility = Visibility.Collapsed;
                FieldPreviewImage.Source = null;
                return;
            }

            if (!explicitPreview && automatic != null)
                FieldPreviewExpressionComboBox.SelectedItem = automatic;

            CalculatorEngine.CompiledExpression? expression = GetFieldPreviewExpression(out GraphExpression? source);
            if (expression == null || source == null)
            {
                FieldPreviewImage.Visibility = Visibility.Collapsed;
                return;
            }

            if (!TryReadExpressionDomain(source, variables, out ExpressionDomain domain, out _, includeYOverride: true))
            {
                FieldPreviewImage.Visibility = Visibility.Collapsed;
                return;
            }

            int res = GetFieldPreviewResolution();
            BitmapSource bitmap = BuildFieldBitmap(expression, variables, domain, res, res, GetFieldPaletteName(), cropToDomain: false);
            FieldPreviewImage.Source = bitmap;
            FieldPreviewImage.Visibility = Visibility.Visible;
        }

        private string GetFieldPaletteName() =>
            (FieldPreviewPaletteComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Signed";

        private BitmapSource BuildFieldBitmap(
            CalculatorEngine.CompiledExpression expression,
            IDictionary<string, double> variables,
            ExpressionDomain domain,
            int width,
            int height,
            string palette,
            bool cropToDomain)
        {
            width = Math.Clamp(width, 32, 1024);
            height = Math.Clamp(height, 32, 1024);

            double domainMinX = Math.Max(_viewport.MinX, domain.MinX ?? _viewport.MinX);
            double domainMaxX = Math.Min(_viewport.MaxX, domain.MaxX ?? _viewport.MaxX);
            double domainMinY = Math.Max(_viewport.MinY, domain.MinY ?? _viewport.MinY);
            double domainMaxY = Math.Min(_viewport.MaxY, domain.MaxY ?? _viewport.MaxY);
            if (!(domainMinX < domainMaxX) || !(domainMinY < domainMaxY))
                return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 255, 255, 255 }, 4);

            // On-screen fields keep the plot's coordinate system. Exported textures deliberately crop to the function domain.
            double minX = cropToDomain ? domainMinX : _viewport.MinX;
            double maxX = cropToDomain ? domainMaxX : _viewport.MaxX;
            double minY = cropToDomain ? domainMinY : _viewport.MinY;
            double maxY = cropToDomain ? domainMaxY : _viewport.MaxY;

            var values = new double[width * height];
            double min = double.PositiveInfinity, max = double.NegativeInfinity, maxAbs = 0;
            for (int py = 0; py < height; py++)
            {
                double y = maxY - (maxY - minY) * py / Math.Max(1, height - 1.0);
                for (int px = 0; px < width; px++)
                {
                    double x = minX + (maxX - minX) * px / Math.Max(1, width - 1.0);
                    bool inDomain = x >= domainMinX && x <= domainMaxX && y >= domainMinY && y <= domainMaxY;
                    double value;
                    if (!inDomain) value = double.NaN;
                    else
                    {
                        try { value = expression.Evaluate(x, y, variables); }
                        catch { value = double.NaN; }
                    }
                    values[py * width + px] = value;
                    if (!double.IsFinite(value)) continue;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                    maxAbs = Math.Max(maxAbs, Math.Abs(value));
                }
            }

            if (!double.IsFinite(min) || !double.IsFinite(max)) { min = 0; max = 1; }
            if (Math.Abs(max - min) < 1e-12) { min -= 0.5; max += 0.5; }
            if (maxAbs < 1e-12) maxAbs = 1;

            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < values.Length; i++)
            {
                double value = values[i];
                int o = i * 4;
                if (!double.IsFinite(value))
                {
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = 0;
                    pixels[o + 3] = 0;
                    continue;
                }

                (byte r, byte g, byte b) = palette switch
                {
                    "Grayscale" => Gray((value - min) / (max - min)),
                    "Heat" => Heat((value - min) / (max - min)),
                    _ => Signed(value / maxAbs)
                };
                pixels[o] = b;
                pixels[o + 1] = g;
                pixels[o + 2] = r;
                pixels[o + 3] = 255;
            }

            BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private static (byte r, byte g, byte b) Gray(double t)
        {
            byte v = (byte)Math.Round(Math.Clamp(t, 0, 1) * 255);
            return (v, v, v);
        }

        private static (byte r, byte g, byte b) Signed(double value)
        {
            double t = Math.Clamp(value, -1, 1);
            if (t < 0)
            {
                byte warm = (byte)Math.Round((1 + t) * 245);
                return (warm, warm, 255);
            }
            byte cool = (byte)Math.Round((1 - t) * 245);
            return (255, cool, cool);
        }

        private static (byte r, byte g, byte b) Heat(double t)
        {
            t = Math.Clamp(t, 0, 1);
            double r = Math.Clamp(1.5 - Math.Abs(4 * t - 3), 0, 1);
            double g = Math.Clamp(1.5 - Math.Abs(4 * t - 2), 0, 1);
            double b = Math.Clamp(1.5 - Math.Abs(4 * t - 1), 0, 1);
            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private void ExportFieldTextureButton_Click(object sender, RoutedEventArgs e)
        {
            CalculatorEngine.CompiledExpression? expression = GetFieldPreviewExpression(out GraphExpression? source);
            if (expression == null || source == null)
            {
                GraphStatusText.Text = "Choose a scalar field first";
                return;
            }

            Dictionary<string, double> variables = GetParameterValues();
            if (!TryReadExpressionDomain(source, variables, out ExpressionDomain domain, out string? error, includeYOverride: true))
            {
                GraphStatusText.Text = error ?? "Invalid field domain";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export 2D field texture",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                FileName = "graph-field.png"
            };
            if (dialog.ShowDialog(this) != true) return;

            BitmapSource bitmap = BuildFieldBitmap(expression, variables, domain, 512, 512, GetFieldPaletteName(), cropToDomain: true);
            SaveBitmap(dialog.FileName, bitmap, new PngBitmapEncoder());
            GraphStatusText.Text = "Texture exported";
        }

        private void AnalyzeExpressionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ComparisonAComboBox?.SelectedItem is not GraphExpression source || source.Kind != GraphExpressionKind.Scalar)
            {
                GraphStatusText.Text = "Analysis needs a 1D scalar function in A";
                return;
            }

            CalculatorEngine.CompiledExpression? sourceCompiled = source.Compiled;
            if (sourceCompiled == null || sourceCompiled.DependsOnY)
            {
                GraphStatusText.Text = "Analysis needs a 1D scalar function in A";
                return;
            }

            Dictionary<string, double> variables = GetParameterValues();
            if (!TryReadExpressionDomain(source, variables, out ExpressionDomain domain, out _, includeYOverride: false)) return;
            double minX = Math.Max(_viewport.MinX, domain.MinX ?? _viewport.MinX);
            double maxX = Math.Min(_viewport.MaxX, domain.MaxX ?? _viewport.MaxX);
            if (!(minX < maxX)) return;

            List<double> roots = FindRoots(sourceCompiled, variables, minX, maxX, 1800);
            List<(double x, double y)> extrema = FindExtrema(sourceCompiled, variables, minX, maxX, 1800);
            List<double> intersections = [];
            if (ComparisonBComboBox?.SelectedItem is GraphExpression other && other.Kind == GraphExpressionKind.Scalar)
            {
                CalculatorEngine.CompiledExpression? otherCompiled = other.Compiled;
                if (otherCompiled != null && !otherCompiled.DependsOnY)
                    intersections = FindIntersections(sourceCompiled, otherCompiled, variables, minX, maxX, 1800);
            }

            string FormatXs(IEnumerable<double> xs) => string.Join(", ", xs.Take(8).Select(NumberFormatting.Format));
            var text = new StringBuilder();
            text.Append("Roots: ").Append(roots.Count == 0 ? "none in view" : FormatXs(roots));
            text.AppendLine();
            text.Append("Extrema: ");
            if (extrema.Count == 0) text.Append("none in view");
            else text.Append(string.Join(", ", extrema.Take(8).Select(p => $"({NumberFormatting.Format(p.x)}, {NumberFormatting.Format(p.y)})")));
            if (intersections.Count > 0)
            {
                text.AppendLine();
                text.Append("A/B intersections: ").Append(FormatXs(intersections));
            }

            AnalysisResultsText.Text = text.ToString();
            AnalysisResultsPanel.Visibility = Visibility.Visible;
        }

        private static List<double> FindRoots(CalculatorEngine.CompiledExpression expression, IDictionary<string, double> variables, double minX, double maxX, int samples)
        {
            return FindZeros(x => SafeEval(expression, x, variables), minX, maxX, samples);
        }

        private static List<double> FindIntersections(CalculatorEngine.CompiledExpression a, CalculatorEngine.CompiledExpression b, IDictionary<string, double> variables, double minX, double maxX, int samples)
        {
            return FindZeros(x => SafeEval(a, x, variables) - SafeEval(b, x, variables), minX, maxX, samples);
        }

        private static List<double> FindZeros(Func<double, double> f, double minX, double maxX, int samples)
        {
            var roots = new List<double>();
            double previousX = minX;
            double previousY = f(previousX);
            for (int i = 1; i <= samples; i++)
            {
                double x = minX + (maxX - minX) * i / samples;
                double y = f(x);
                if (double.IsFinite(previousY) && double.IsFinite(y))
                {
                    if (Math.Abs(y) < 1e-8) AddUnique(roots, x, (maxX - minX) / samples * 2);
                    if ((previousY < 0) != (y < 0))
                    {
                        double lo = previousX, hi = x, flo = previousY;
                        for (int k = 0; k < 35; k++)
                        {
                            double mid = (lo + hi) * 0.5;
                            double fm = f(mid);
                            if (!double.IsFinite(fm)) break;
                            if ((flo < 0) != (fm < 0)) hi = mid;
                            else { lo = mid; flo = fm; }
                        }
                        AddUnique(roots, (lo + hi) * 0.5, (maxX - minX) / samples * 2);
                    }
                }
                previousX = x;
                previousY = y;
            }
            return roots;
        }

        private static List<(double x, double y)> FindExtrema(CalculatorEngine.CompiledExpression expression, IDictionary<string, double> variables, double minX, double maxX, int samples)
        {
            var result = new List<(double x, double y)>();
            double h = (maxX - minX) / samples;
            if (!(h > 0)) return result;
            double previousSlope = double.NaN;
            for (int i = 1; i < samples; i++)
            {
                double x = minX + h * i;
                double yl = SafeEval(expression, x - h, variables);
                double yr = SafeEval(expression, x + h, variables);
                double y = SafeEval(expression, x, variables);
                if (!double.IsFinite(yl) || !double.IsFinite(yr) || !double.IsFinite(y)) { previousSlope = double.NaN; continue; }
                double slope = (yr - yl) / (2 * h);
                if (double.IsFinite(previousSlope) && (previousSlope < 0) != (slope < 0) && Math.Abs(previousSlope - slope) > 1e-8)
                {
                    if (result.Count == 0 || Math.Abs(result[^1].x - x) > h * 3) result.Add((x, y));
                }
                previousSlope = slope;
            }
            return result;
        }

        private static double SafeEval(CalculatorEngine.CompiledExpression expression, double x, IDictionary<string, double> variables)
        {
            try { return expression.Evaluate(x, variables); }
            catch { return double.NaN; }
        }

        private static void AddUnique(List<double> values, double value, double tolerance)
        {
            if (values.Count == 0 || Math.Abs(values[^1] - value) > tolerance) values.Add(value);
        }

        private void CaptureParameterStateAButton_Click(object sender, RoutedEventArgs e) => CaptureParameterState(_parameterStateA, "A");
        private void CaptureParameterStateBButton_Click(object sender, RoutedEventArgs e) => CaptureParameterState(_parameterStateB, "B");

        private void CaptureParameterState(Dictionary<string, double> target, string label)
        {
            target.Clear();
            foreach (GraphParameter parameter in Parameters) target[parameter.Name] = parameter.Value;
            GraphStatusText.Text = $"Captured parameter state {label}";
            MarkWorkspaceEdit();
        }

        private void ParameterStateBlendSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateParameterStateBlendText();
            if (_applyingParameterState || _parameterStateA.Count == 0 || _parameterStateB.Count == 0) return;

            _applyingParameterState = true;
            try
            {
                double t = Math.Clamp(e.NewValue, 0, 1);
                foreach (GraphParameter parameter in Parameters)
                {
                    if (parameter.IsLocked) continue;
                    if (!_parameterStateA.TryGetValue(parameter.Name, out double a) || !_parameterStateB.TryGetValue(parameter.Name, out double b)) continue;
                    parameter.SetValueIgnoringLock(a + (b - a) * t);
                }
            }
            finally { _applyingParameterState = false; }
            RefreshAllExpressionStatuses();
            ScheduleRender(10);
        }

        private void UpdateParameterStateBlendText()
        {
            if (ParameterStateBlendText == null || ParameterStateBlendSlider == null) return;
            ParameterStateBlendText.Text = $"{Math.Round(ParameterStateBlendSlider.Value * 100):0}%";
        }

        private void OpenFunctionSuggestions(TextBox box)
        {
            int caret = box.CaretIndex;
            int start = caret;
            while (start > 0 && (char.IsLetterOrDigit(box.Text[start - 1]) || box.Text[start - 1] == '_')) start--;
            string prefix = box.Text[start..caret];
            List<FunctionHelp> matches = FunctionCatalog.Items
                .Where(item => string.IsNullOrEmpty(prefix) || item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Take(12)
                .ToList();
            if (matches.Count == 0) matches = FunctionCatalog.Items.Take(12).ToList();

            var menu = new ContextMenu();
            foreach (FunctionHelp help in matches)
            {
                var item = new MenuItem { Header = help.Signature, ToolTip = help.Description, Tag = help };
                item.Click += (_, _) =>
                {
                    string insertion = help.Name + "(";
                    box.Select(start, caret - start);
                    box.SelectedText = insertion;
                    box.CaretIndex = start + insertion.Length;
                    box.Focus();
                };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = box;
            menu.IsOpen = true;
        }

        private void ExpressionBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitPendingUndoGroup();
        }

        private void MarkWorkspaceEdit()
        {
            if (!_productPassReady || _suppressUndo || _loadingWorkspace) return;
            MarkProjectDirty();
            _pendingUndoBefore ??= _editBaseline ?? CaptureEditSnapshot();
            _undoCaptureTimer?.Stop();
            _undoCaptureTimer?.Start();
        }

        private void CommitPendingUndoGroup()
        {
            _undoCaptureTimer?.Stop();
            if (_pendingUndoBefore == null) return;
            EditSnapshot current = CaptureEditSnapshot();
            if (!SnapshotsEqual(_pendingUndoBefore, current))
            {
                _undoStack.Push(_pendingUndoBefore);
                _redoStack.Clear();
            }
            _editBaseline = current;
            _pendingUndoBefore = null;
        }

        private EditSnapshot CaptureEditSnapshot()
        {
            return new EditSnapshot(
                _is3DMode,
                _viewport,
                _surfaceViewport,
                _timelineTime,
                Expressions.Select(e => new WorkspaceExpression
                {
                    Expression = e.Expression,
                    IsVisible = e.IsVisible,
                    DomainMinX = e.DomainMinX,
                    DomainMaxX = e.DomainMaxX,
                    DomainMinY = e.DomainMinY,
                    DomainMaxY = e.DomainMaxY
                }).ToList(),
                Parameters.Select(p => new WorkspaceParameter
                {
                    Name = p.Name,
                    Minimum = p.Minimum,
                    Maximum = p.Maximum,
                    Value = p.Value,
                    IsAnimating = p.IsAnimating,
                    AnimationSpeed = p.AnimationSpeed,
                    AnimationMode = p.AnimationMode,
                    DisplayName = p.DisplayName,
                    Group = p.Group,
                    Unit = p.Unit,
                    IsLocked = p.IsLocked
                }).ToList(),
                CurveChannels.Select(ToWorkspaceCurveChannel).ToList(),
                _activeCurveChannel?.Id ?? Guid.Empty,
                _curveBeforeMode.ToString(),
                _curveAfterMode.ToString(),
                _curveAutoTension,
                SharedAssets.Select(ToWorkspaceSharedAsset).ToList(),
                ImportedTables.Select(t => new WorkspaceImportedTable { Name = t.Name, XUnit = t.XUnit, YUnit = t.YUnit, Rows = t.Rows.ToList() }).ToList(),
                EconomyNodes.Select(ToWorkspaceEconomyNode).ToList(),
                EconomyLinks.Select(ToWorkspaceEconomyLink).ToList(),
                EconomyParameters.Select(p => new WorkspaceEconomyParameter { Name = p.Name, Minimum = p.Minimum, Maximum = p.Maximum, Value = p.Value }).ToList(),
                EconomyScenarios.Select(x => new WorkspaceEconomyScenario { Name = x.Name, ParameterValues = new Dictionary<string, double>(x.ParameterValues, StringComparer.OrdinalIgnoreCase) }).ToList(),
                EconomyCohorts.Select(x => new WorkspaceEconomyCohort { Name = x.Name, Weight = x.Weight, ParameterValues = new Dictionary<string, double>(x.ParameterValues, StringComparer.OrdinalIgnoreCase) }).ToList(),
                EconomyTargets.Select(x => new WorkspaceEconomyTarget { NodeId = x.NodeId, NodeName = x.NodeName, Minimum = x.Minimum, Maximum = x.Maximum, Weight = x.Weight }).ToList(),
                EconomySubsystems.Select(ToWorkspaceSubsystem).ToList(),
                EconomyRecipes.Select(ToWorkspaceRecipe).ToList(),
                EconomyResourceStyles.Select(ToWorkspaceResourceStyle).ToList());
        }

        private static bool SnapshotsEqual(EditSnapshot a, EditSnapshot b) =>
            JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

        private void UndoWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            CommitPendingUndoGroup();
            if (_undoStack.Count == 0) { GraphStatusText.Text = "Nothing to undo"; return; }
            EditSnapshot current = CaptureEditSnapshot();
            EditSnapshot previous = _undoStack.Pop();
            _redoStack.Push(current);
            RestoreEditSnapshot(previous);
            GraphStatusText.Text = "Undo";
        }

        private void RedoWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            CommitPendingUndoGroup();
            if (_redoStack.Count == 0) { GraphStatusText.Text = "Nothing to redo"; return; }
            EditSnapshot current = CaptureEditSnapshot();
            EditSnapshot next = _redoStack.Pop();
            _undoStack.Push(current);
            RestoreEditSnapshot(next);
            GraphStatusText.Text = "Redo";
        }

        private void RestoreEditSnapshot(EditSnapshot snapshot)
        {
            _suppressUndo = true;
            _loadingWorkspace = true;
            try
            {
                ClearWorkspaceCollections();
                CurveKeys.Clear(); ClearEconomyModel(); CurveChannels.Clear(); SharedAssets.Clear(); ImportedTables.Clear(); EconomySubsystems.Clear(); EconomyRecipes.Clear(); EconomyResourceStyles.Clear();
                _viewport = snapshot.PlotView;
                _surfaceViewport = snapshot.SurfaceView;
                _timelineTime = snapshot.TimelineTime;
                _is3DMode = snapshot.Is3D;
                PlotModeComboBox.SelectedIndex = _is3DMode ? 1 : 0;

                foreach (WorkspaceExpression source in snapshot.Expressions)
                {
                    GraphExpression item = AddExpression(source.Expression);
                    item.IsVisible = source.IsVisible;
                    item.DomainMinX = source.DomainMinX;
                    item.DomainMaxX = source.DomainMaxX;
                    item.DomainMinY = source.DomainMinY;
                    item.DomainMaxY = source.DomainMaxY;
                }
                if (Expressions.Count == 0) AddExpression();
                SyncParameters();
                foreach (WorkspaceParameter source in snapshot.Parameters)
                {
                    GraphParameter? parameter = Parameters.FirstOrDefault(p => p.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    if (parameter == null) continue;
                    parameter.SetRangeAndValue(source.Minimum, source.Maximum, source.Value);
                    parameter.DisplayName = source.DisplayName;
                    parameter.Group = source.Group;
                    parameter.Unit = source.Unit;
                    parameter.AnimationSpeed = source.AnimationSpeed;
                    parameter.AnimationMode = source.AnimationMode;
                    parameter.IsAnimating = source.IsAnimating;
                    parameter.IsLocked = source.IsLocked;
                }
                _curveBeforeMode = Enum.TryParse(snapshot.CurveBeforeMode, true, out CurveExtrapolationMode undoBefore) ? undoBefore : CurveExtrapolationMode.Clamp;
                _curveAfterMode = Enum.TryParse(snapshot.CurveAfterMode, true, out CurveExtrapolationMode undoAfter) ? undoAfter : CurveExtrapolationMode.Clamp;
                _curveAutoTension = Math.Clamp(snapshot.CurveAutoTension, 0, 1);
                CurveDesignerMath.AutoTension = _curveAutoTension;
                SetCurveWrapCombo(CurveBeforeModeComboBox, _curveBeforeMode);
                SetCurveWrapCombo(CurveAfterModeComboBox, _curveAfterMode);
                if (CurveTensionSlider != null) CurveTensionSlider.Value = _curveAutoTension;
                foreach (WorkspaceSharedAsset source in snapshot.SharedAssets) SharedAssets.Add(FromWorkspaceSharedAsset(source));
                foreach (WorkspaceImportedTable source in snapshot.ImportedTables) ImportedTables.Add(new ImportedTable { Name = source.Name, XUnit = source.XUnit, YUnit = source.YUnit, Rows = source.Rows?.ToList() ?? [] });
                foreach (WorkspaceCurveChannel source in snapshot.CurveChannels) CurveChannels.Add(FromWorkspaceCurveChannel(source));
                _activeCurveChannel = CurveChannels.FirstOrDefault(c => c.Id == snapshot.ActiveCurveChannelId) ?? CurveChannels.FirstOrDefault();
                CurveChannelsComboBox.SelectedItem = _activeCurveChannel;
                if (_activeCurveChannel != null) LoadActiveCurveChannel();
                foreach (WorkspaceEconomyNode source in snapshot.EconomyNodes) EconomyNodes.Add(FromWorkspaceEconomyNode(source));
                foreach (WorkspaceEconomyLink source in snapshot.EconomyLinks) EconomyLinks.Add(FromWorkspaceEconomyLink(source));
                SyncEconomyParameters();
                foreach (WorkspaceEconomyParameter source in snapshot.EconomyParameters)
                {
                    EconomyParameter? parameter = EconomyParameters.FirstOrDefault(p => p.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    if (parameter == null) continue;
                    parameter.Minimum = source.Minimum; parameter.Maximum = source.Maximum; parameter.Value = source.Value;
                }
                foreach (WorkspaceEconomyScenario source in snapshot.EconomyScenarios) EconomyScenarios.Add(new EconomyScenario { Name = source.Name, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
                foreach (WorkspaceEconomyCohort source in snapshot.EconomyCohorts) EconomyCohorts.Add(new EconomyCohort { Name = source.Name, Weight = source.Weight, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
                foreach (WorkspaceEconomyTarget source in snapshot.EconomyTargets) EconomyTargets.Add(new EconomyTarget { NodeId = source.NodeId, NodeName = source.NodeName, Minimum = source.Minimum, Maximum = source.Maximum, Weight = source.Weight });
                foreach (WorkspaceEconomySubsystem source in snapshot.EconomySubsystems) EconomySubsystems.Add(FromWorkspaceSubsystem(source));
                foreach (WorkspaceEconomyRecipe source in snapshot.EconomyRecipes) EconomyRecipes.Add(FromWorkspaceRecipe(source));
                foreach (WorkspaceEconomyResourceStyle source in snapshot.EconomyResourceStyles) EconomyResourceStyles.Add(FromWorkspaceResourceStyle(source));
            }
            finally
            {
                _loadingWorkspace = false;
                _suppressUndo = false;
            }

            RefreshParameterGroups();
            UpdatePlotRangeInputs();
            UpdateSurfaceRangeInputs();
            UpdateTimelineUi();
            UpdatePlotModeUi(refreshExpressions: true);
            UpdateSurfaceCamera();
            _editBaseline = CaptureEditSnapshot();
            ScheduleRender(0);
        }

        private void CapturePngButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Title = "Capture graph", Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", FileName = "graph.png" };
            if (dialog.ShowDialog(this) != true) return;
            BitmapSource bitmap = CaptureGraphBitmap(1.5);
            SaveBitmap(dialog.FileName, bitmap, new PngBitmapEncoder());
            GraphStatusText.Text = "PNG captured";
        }

        private async void CaptureGifButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Title = "Export timeline animation", Filter = "Animated GIF (*.gif)|*.gif", DefaultExt = ".gif", FileName = "graph-animation.gif" };
            if (dialog.ShowDialog(this) != true) return;

            bool wasPlaying = _timelinePlaying;
            double oldTime = _timelineTime;
            RenderQuality oldQuality = _renderQuality;
            bool[] parameterAnimations = Parameters.Select(p => p.IsAnimating).ToArray();
            _timelinePlaying = false;
            for (int i = 0; i < Parameters.Count; i++) Parameters[i].IsAnimating = false;
            _renderQuality = RenderQuality.Draft;

            try
            {
                var encoder = new GifBitmapEncoder();
                const int frames = 36;
                for (int i = 0; i < frames; i++)
                {
                    _timelineTime = _timelineStart + (_timelineEnd - _timelineStart) * i / frames;
                    UpdateTimelineUi();
                    await RenderGraphAsync();
                    await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Render);
                    BitmapSource bitmap = CaptureGraphBitmap(0.8);
                    var metadata = new BitmapMetadata("gif");
                    metadata.SetQuery("/grctlext/Delay", (ushort)3);
                    metadata.SetQuery("/grctlext/Disposal", (byte)2);
                    encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
                    GraphStatusText.Text = $"GIF {i + 1}/{frames}";
                }
                using FileStream stream = File.Create(dialog.FileName);
                encoder.Save(stream);
                GraphStatusText.Text = "GIF exported";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not export GIF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _timelineTime = oldTime;
                _renderQuality = oldQuality;
                for (int i = 0; i < Parameters.Count && i < parameterAnimations.Length; i++) Parameters[i].IsAnimating = parameterAnimations[i];
                _timelinePlaying = wasPlaying;
                UpdateTimelineUi();
                UpdateAnimationTimerState();
                ScheduleRender(0);
            }
        }

        private BitmapSource CaptureGraphBitmap(double scale)
        {
            int width = Math.Max(1, (int)Math.Round(GraphSurface.ActualWidth * scale));
            int height = Math.Max(1, (int)Math.Round(GraphSurface.ActualHeight * scale));
            var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(GraphSurface);
            bitmap.Freeze();
            return bitmap;
        }

        private static void SaveBitmap(string path, BitmapSource bitmap, BitmapEncoder encoder)
        {
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
        }

        private string BuildHlslExport(GraphExpression source)
        {
            CalculatorEngine.CompiledExpression expression = source.Compiled ?? source.Components.First();
            string body = expression.ToHlsl();

            bool usesX = expression.Variables.Contains("x", StringComparer.OrdinalIgnoreCase);
            bool usesY = expression.Variables.Contains("y", StringComparer.OrdinalIgnoreCase);
            bool usesZ = expression.Variables.Contains("z", StringComparer.OrdinalIgnoreCase);
            string[] variables = expression.Variables
                .Where(name => !name.Equals("x", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("y", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("z", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("t", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("time", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var arguments = new List<string>();
            if (usesX || usesY || usesZ) arguments.Add("float x");
            if (usesY || usesZ) arguments.Add("float y");
            if (usesZ) arguments.Add("float z");
            if (expression.Variables.Contains("t", StringComparer.OrdinalIgnoreCase)
                || expression.Variables.Contains("time", StringComparer.OrdinalIgnoreCase))
                arguments.Add("float time");
            arguments.AddRange(variables.Select(v => "float " + v));
            if (arguments.Count == 0) arguments.Add("float x");

            // Keep t and time interchangeable in the editor while emitting one predictable shader argument.
            body = ReplaceHlslIdentifier(body, "t", "time");

            var sb = new StringBuilder();
            sb.AppendLine("// Graph Calculator HLSL export");
            sb.Append("// Source: ").AppendLine(source.Expression.Replace("\r", " ").Replace("\n", " "));
            sb.AppendLine("float gc_inverseLerp(float a, float b, float v) { return abs(b-a) < 1e-7 ? 0.0 : (v-a)/(b-a); }");
            sb.AppendLine("float gc_smootherstep(float a, float b, float v) { float t=saturate(gc_inverseLerp(a,b,v)); return t*t*t*(t*(t*6.0-15.0)+10.0); }");
            sb.AppendLine("float gc_remap(float a,float b,float c,float d,float v) { return lerp(c,d,gc_inverseLerp(a,b,v)); }");
            sb.AppendLine("float gc_pingpong(float v,float len) { if(abs(len)<1e-7) return 0.0; float r=v-floor(v/(2.0*len))*(2.0*len); return len-abs(r-len); }");
            sb.AppendLine("float gc_hash(float2 p,float seed) { return frac(sin(dot(p,float2(127.1,311.7))+seed*74.7)*43758.5453123); }");
            sb.AppendLine("float gc_noise(float2 p,float seed) { float2 i=floor(p); float2 f=frac(p); f=f*f*f*(f*(f*6.0-15.0)+10.0); float a=gc_hash(i,seed), b=gc_hash(i+float2(1,0),seed), c=gc_hash(i+float2(0,1),seed), d=gc_hash(i+1,seed); return (lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y)*2.0)-1.0; }");
            sb.AppendLine("float gc_fbm(float2 p,float octaves,float persistence,float lacunarity) { float amp=1.0,freq=1.0,total=0.0,weight=0.0; [loop] for(int i=0;i<10;i++){ if(i>=(int)round(octaves)) break; total+=gc_noise(p*freq,i*101.0)*amp; weight+=amp; amp*=saturate(persistence); freq*=max(lacunarity,1.01); } return weight>0.0?total/weight:0.0; }");
            sb.AppendLine();
            sb.Append("float GraphFunction(").Append(string.Join(", ", arguments)).AppendLine(")");
            sb.AppendLine("{");
            sb.Append("    return ").Append(body).AppendLine(";");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ReplaceHlslIdentifier(string source, string identifier, string replacement)
        {
            if (identifier.Equals(replacement, StringComparison.Ordinal)) return source;
            var result = new StringBuilder(source.Length + 8);
            for (int i = 0; i < source.Length;)
            {
                if ((char.IsLetter(source[i]) || source[i] == '_'))
                {
                    int start = i++;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                    string token = source[start..i];
                    result.Append(token.Equals(identifier, StringComparison.OrdinalIgnoreCase) ? replacement : token);
                }
                else
                {
                    result.Append(source[i++]);
                }
            }
            return result.ToString();
        }

    }
}
