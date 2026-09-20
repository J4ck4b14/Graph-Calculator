using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private CurveKey? _selectedCurveKey;
        private CurveKey? _dragCurveKey;
        private string? _dragCurveHandle;
        private CurveExtrapolationMode _curveBeforeMode = CurveExtrapolationMode.Clamp;
        private CurveExtrapolationMode _curveAfterMode = CurveExtrapolationMode.Clamp;

        public ObservableCollection<CurveFunctionSuggestion> CurveSuggestions { get; } = new();

        private void InitializeCurveDesigner()
        {
            CurveKeys.CollectionChanged += CurveKeys_CollectionChanged;
            if (CurveBeforeModeComboBox != null) CurveBeforeModeComboBox.SelectedIndex = 0;
            if (CurveAfterModeComboBox != null) CurveAfterModeComboBox.SelectedIndex = 0;
            UpdateCurveDesignerFormula();
            RefreshCurveSuggestions();
        }

        private void CurveKeys_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (CurveKey key in e.OldItems) key.PropertyChanged -= CurveKey_PropertyChanged;
            if (e.NewItems != null)
                foreach (CurveKey key in e.NewItems) key.PropertyChanged += CurveKey_PropertyChanged;

            UpdateCurveDesignerFormula();
            RefreshCurveSuggestions();
            if (!_loadingWorkspace) { SaveActiveCurveChannel(); MarkWorkspaceEdit(); }
            if (_workspaceMode == WorkspaceMode.CurveDesigner) RenderCurveDesigner();
        }

        private void CurveKey_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateCurveDesignerFormula();
            if (!_loadingWorkspace) { SaveActiveCurveChannel(); MarkWorkspaceEdit(); }
            if (_workspaceMode == WorkspaceMode.CurveDesigner) RenderCurveDesigner();
        }

        private void CurveAddExactButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseCurveNumber(CurveNewXTextBox.Text, out double x) || !TryParseCurveNumber(CurveNewYTextBox.Text, out double y))
            {
                CurveGraphStatusText.Text = "Enter valid X and Y values";
                return;
            }
            AddCurveKey(x, y);
        }

        private void CurveDeleteKeyButton_Click(object sender, RoutedEventArgs e)
        {
            CurveKey? key = _selectedCurveKey ?? CurveKeysDataGrid.SelectedItem as CurveKey;
            if (key == null) return;
            CurveKeys.Remove(key);
            _selectedCurveKey = null;
            CurveKeysDataGrid.SelectedItem = null;
            RefreshCurveSuggestions();
        }

        private void CurveClearButton_Click(object sender, RoutedEventArgs e)
        {
            CurveKeys.Clear();
            _selectedCurveKey = null;
            CurveSuggestions.Clear();
            CurveGraphStatusText.Text = "Click the graph to place the first key";
        }

        private void CurveKeysDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedCurveKey = CurveKeysDataGrid.SelectedItem as CurveKey;
            if (_workspaceMode == WorkspaceMode.CurveDesigner) RenderCurveDesigner();
        }

        private void CurveKeysDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCurveDesignerFormula();
                RefreshCurveSuggestions();
                RenderCurveDesigner();
            }));
        }

        private void CurveTangentModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCurveDesignerFormula();
                RefreshCurveSuggestions();
                if (_workspaceMode == WorkspaceMode.CurveDesigner) RenderCurveDesigner();
            }));
        }

        private void CurveWrapModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace) return;
            _curveBeforeMode = ReadCurveWrapMode(CurveBeforeModeComboBox);
            _curveAfterMode = ReadCurveWrapMode(CurveAfterModeComboBox);
            UpdateCurveDesignerFormula();
            MarkWorkspaceEdit();
            if (_workspaceMode == WorkspaceMode.CurveDesigner) RenderCurveDesigner();
        }

        private void CurveCopyFormulaButton_Click(object sender, RoutedEventArgs e)
        {
            string formula = CurveFormulaTextBox.Text;
            if (string.IsNullOrWhiteSpace(formula) || formula.StartsWith("/*")) return;
            Clipboard.SetText(formula);
            CurveGraphStatusText.Text = "Formula copied";
        }

        private void CurveSendToFunctionLabButton_Click(object sender, RoutedEventArgs e)
        {
            string formula = CurveDesignerMath.BuildPiecewiseFormula(CurveKeys, _curveBeforeMode, _curveAfterMode);
            List<CurveKey> ordered = CurveDesignerMath.Ordered(CurveKeys);
            if (ordered.Count < 2 || !CurveDesignerMath.HasDistinctX(ordered) || string.IsNullOrWhiteSpace(formula))
            {
                CurveGraphStatusText.Text = "At least two keys with distinct X values are required";
                return;
            }

            GraphExpression item = AddExpression(formula);
            _is3DMode = false;
            PlotModeComboBox.SelectedIndex = 0;
            SetWorkspaceMode(WorkspaceMode.FunctionLab, updateCombo: true);
            GraphStatusText.Text = "Exact spline added to Function Lab";
            FocusExpression(item, moveCaretToEnd: false);
        }

        private void CurveSuggestionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_workspaceMode == WorkspaceMode.CurveDesigner) RenderCurveDesigner();
        }

        private void CurveUseSuggestionButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurveSuggestionsListBox.SelectedItem is not CurveFunctionSuggestion suggestion)
            {
                CurveGraphStatusText.Text = "Select a suggested function first";
                return;
            }

            List<CurveKey> ordered = CurveDesignerMath.Ordered(CurveKeys);
            if (ordered.Count < 2) return;
            GraphExpression item = AddExpression(suggestion.Formula);
            item.DomainMinX = ordered[0].X.ToString("G17", CultureInfo.InvariantCulture);
            item.DomainMaxX = ordered[^1].X.ToString("G17", CultureInfo.InvariantCulture);
            _is3DMode = false;
            PlotModeComboBox.SelectedIndex = 0;
            SetWorkspaceMode(WorkspaceMode.FunctionLab, updateCombo: true);
            GraphStatusText.Text = $"{suggestion.Name} approximation added to Function Lab";
            FocusExpression(item, moveCaretToEnd: false);
        }

        private void CurveCopySuggestionButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurveSuggestionsListBox.SelectedItem is not CurveFunctionSuggestion suggestion) return;
            Clipboard.SetText(suggestion.Formula);
            CurveGraphStatusText.Text = $"{suggestion.Name} formula copied";
        }

        private void FitCurveDesignerButton_Click(object sender, RoutedEventArgs e)
        {
            List<CurveKey> keys = CurveDesignerMath.Ordered(CurveKeys);
            if (keys.Count == 0) return;
            double keyMinX = keys.Min(k => k.X), keyMaxX = keys.Max(k => k.X);
            if (Math.Abs(keyMaxX - keyMinX) < 1e-9) { keyMinX -= 1; keyMaxX += 1; }

            double span = keyMaxX - keyMinX;
            double beforePad = IsCyclingMode(_curveBeforeMode) ? span : span * (_curveBeforeMode == CurveExtrapolationMode.Continue ? 0.35 : 0.12);
            double afterPad = IsCyclingMode(_curveAfterMode) ? span : span * (_curveAfterMode == CurveExtrapolationMode.Continue ? 0.35 : 0.12);
            double minX = keyMinX - beforePad;
            double maxX = keyMaxX + afterPad;

            List<GraphPoint> samples = keys.Count >= 2 && CurveDesignerMath.HasDistinctX(keys)
                ? CurveDesignerMath.SampleRange(keys, minX, maxX, _curveBeforeMode, _curveAfterMode, 640)
                : CurveDesignerMath.Sample(keys);
            double minY = samples.Where(p => double.IsFinite(p.Y)).Select(p => p.Y).DefaultIfEmpty(keys.Min(k => k.Y)).Min();
            double maxY = samples.Where(p => double.IsFinite(p.Y)).Select(p => p.Y).DefaultIfEmpty(keys.Max(k => k.Y)).Max();
            if (Math.Abs(maxY - minY) < 1e-9) { minY -= 1; maxY += 1; }
            double py = (maxY - minY) * 0.18;
            _viewport = new PlotViewport(minX, maxX, minY - py, maxY + py);
            UpdatePlotRangeInputs();
            UpdateViewRangeText();
            RenderCurveDesigner();
        }

        private void AddCurveKey(double x, double y)
        {
            if (CurveSnapCheckBox?.IsChecked == true && TryParseCurveNumber(CurveSnapStepTextBox.Text, out double step) && step > 0)
            {
                x = Math.Round(x / step) * step;
                y = Math.Round(y / step) * step;
            }

            if (CurveKeys.Any(k => Math.Abs(k.X - x) < 1e-8))
            {
                CurveGraphStatusText.Text = "Function keys need distinct X values";
                return;
            }

            var key = new CurveKey { X = x, Y = y, TangentMode = "Auto" };
            CurveKeys.Add(key);
            _selectedCurveKey = key;
            CurveKeysDataGrid.SelectedItem = key;
            CurveKeysDataGrid.ScrollIntoView(key);
            CurveGraphStatusText.Text = $"Key added at ({NumberFormatting.Format(x)}, {NumberFormatting.Format(y)})";
        }

        private void UpdateCurveDesignerFormula()
        {
            if (CurveFormulaTextBox == null || CurveDefinitionTextBox == null) return;
            List<CurveKey> ordered = CurveDesignerMath.Ordered(CurveKeys);
            if (ordered.Count < 2)
            {
                string value = ordered.Count == 0 ? string.Empty : NumberFormatting.Format(ordered[0].Y);
                CurveDefinitionTextBox.Text = value;
                CurveFormulaTextBox.Text = value;
                return;
            }
            CurveDefinitionTextBox.Text = CurveDesignerMath.BuildCompactDefinition(ordered, _curveBeforeMode, _curveAfterMode);
            CurveFormulaTextBox.Text = CurveDesignerMath.BuildPiecewiseFormula(ordered, _curveBeforeMode, _curveAfterMode);
        }

        private void RefreshCurveSuggestions()
        {
            if (CurveSuggestionsListBox == null) return;
            CurveFunctionSuggestion? previous = CurveSuggestionsListBox.SelectedItem as CurveFunctionSuggestion;
            string? previousFormula = previous?.Formula;
            CurveSuggestions.Clear();
            foreach (CurveFunctionSuggestion suggestion in CurveDesignerMath.SuggestFunctions(CurveKeys))
                CurveSuggestions.Add(suggestion);

            if (CurveSuggestions.Count == 0) return;
            CurveFunctionSuggestion? restore = previousFormula == null ? null : CurveSuggestions.FirstOrDefault(s => s.Formula == previousFormula);
            CurveSuggestionsListBox.SelectedItem = restore ?? CurveSuggestions[0];
        }

        private void RenderCurveDesigner()
        {
            if (!IsLoaded || PlotCanvas == null || _workspaceMode != WorkspaceMode.CurveDesigner) return;
            double width = GraphSurface.ActualWidth;
            double height = GraphSurface.ActualHeight;
            if (width < 20 || height < 20) return;

            FieldPreviewImage.Visibility = Visibility.Collapsed;
            SurfaceViewport3D.Visibility = Visibility.Collapsed;
            PlotCanvas.Visibility = Visibility.Visible;
            PlotCanvas.Children.Clear();
            DrawGridAndAxes(_viewport, width, height);
            DrawInactiveCurveChannels(width, height);

            List<CurveKey> ordered = CurveDesignerMath.Ordered(CurveKeys);
            if (ordered.Count >= 2 && CurveDesignerMath.HasDistinctX(ordered))
            {
                DrawSeries(
                    CurveDesignerMath.SampleRange(ordered, _viewport.MinX, _viewport.MaxX, _curveBeforeMode, _curveAfterMode),
                    Brushes.RoyalBlue, _viewport, width, height);
                DrawCurveRangeBoundary(ordered[0].X, width, height);
                DrawCurveRangeBoundary(ordered[^1].X, width, height);
                DrawSelectedCurveSuggestion(ordered, width, height);
            }

            foreach (CurveKey key in ordered)
            {
                double px = TransformX(key.X, _viewport, width);
                double py = TransformY(key.Y, _viewport, height);
                bool selected = ReferenceEquals(key, _selectedCurveKey);
                var dot = new Ellipse
                {
                    Width = selected ? 12 : 9,
                    Height = selected ? 12 : 9,
                    Fill = selected ? Brushes.White : Brushes.RoyalBlue,
                    Stroke = Brushes.RoyalBlue,
                    StrokeThickness = selected ? 3 : 2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, px - dot.Width / 2);
                Canvas.SetTop(dot, py - dot.Height / 2);
                PlotCanvas.Children.Add(dot);
            }

            if (_selectedCurveKey != null && ordered.Contains(_selectedCurveKey))
                DrawCurveTangentHandles(_selectedCurveKey, ordered, width, height);

            CurveGraphStatusText.Text = ordered.Count switch
            {
                0 => "Click the graph to place the first key",
                1 => "Add another key to form a curve",
                _ when !CurveDesignerMath.HasDistinctX(ordered) => "Two keys share the same X value",
                _ => $"{ordered.Count} keys · Hermite · before {CurveModeLabel(_curveBeforeMode)} · after {CurveModeLabel(_curveAfterMode)}"
            };
            UpdateViewRangeText();
        }

        private void DrawCurveTangentHandles(CurveKey key, IReadOnlyList<CurveKey> ordered, double width, double height)
        {
            int index = IndexOfCurveKey(ordered, key);
            if (index < 0) return;
            List<CurveResolvedKey> tangents = CurveDesignerMath.ResolveTangents(ordered);
            CurveResolvedKey resolved = tangents[index];
            double span = _viewport.Width * 0.08;
            if (index > 0) span = Math.Min(span, (key.X - ordered[index - 1].X) * 0.35);
            if (index + 1 < ordered.Count) span = Math.Min(span, (ordered[index + 1].X - key.X) * 0.35);
            span = Math.Max(span, _viewport.Width * 0.015);

            AddCurveHandle(key.X - span, key.Y - resolved.InSlope * span, key, true, width, height);
            AddCurveHandle(key.X + span, key.Y + resolved.OutSlope * span, key, false, width, height);
        }

        private void AddCurveHandle(double hx, double hy, CurveKey key, bool incoming, double width, double height)
        {
            double kx = TransformX(key.X, _viewport, width);
            double ky = TransformY(key.Y, _viewport, height);
            double px = TransformX(hx, _viewport, width);
            double py = TransformY(hy, _viewport, height);
            PlotCanvas.Children.Add(new Line { X1 = kx, Y1 = ky, X2 = px, Y2 = py, Stroke = Brushes.SlateGray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false });
            var handle = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White, Stroke = Brushes.SlateGray, StrokeThickness = 1.5, IsHitTestVisible = false };
            Canvas.SetLeft(handle, px - 4); Canvas.SetTop(handle, py - 4); PlotCanvas.Children.Add(handle);
        }

        private bool CurveDesignerMouseDown(Point point, MouseButtonEventArgs e)
        {
            if (_workspaceMode != WorkspaceMode.CurveDesigner || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return false;
            double width = PlotCanvas.ActualWidth, height = PlotCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return true;

            List<CurveKey> ordered = CurveDesignerMath.Ordered(CurveKeys);
            if (_selectedCurveKey != null && ordered.Contains(_selectedCurveKey))
            {
                var handle = FindCurveHandleNear(point, _selectedCurveKey, ordered, width, height);
                if (handle != null)
                {
                    _dragCurveKey = _selectedCurveKey;
                    _dragCurveHandle = handle;
                    PlotCanvas.CaptureMouse();
                    e.Handled = true;
                    return true;
                }
            }

            CurveKey? nearest = ordered
                .Select(k => new { Key = k, D = PixelDistance(point, k, width, height) })
                .Where(v => v.D <= 13)
                .OrderBy(v => v.D)
                .Select(v => v.Key)
                .FirstOrDefault();

            if (nearest != null)
            {
                _selectedCurveKey = nearest;
                CurveKeysDataGrid.SelectedItem = nearest;
                _dragCurveKey = nearest;
                _dragCurveHandle = null;
                PlotCanvas.CaptureMouse();
                RenderCurveDesigner();
                e.Handled = true;
                return true;
            }

            double x = PixelToX(point.X, _viewport, width);
            double y = PixelToY(point.Y, _viewport, height);
            AddCurveKey(x, y);
            e.Handled = true;
            return true;
        }

        private bool CurveDesignerMouseMove(Point point, MouseEventArgs e)
        {
            if (_workspaceMode != WorkspaceMode.CurveDesigner || _dragCurveKey == null || e.LeftButton != MouseButtonState.Pressed) return false;
            double width = PlotCanvas.ActualWidth, height = PlotCanvas.ActualHeight;
            double x = PixelToX(point.X, _viewport, width);
            double y = PixelToY(point.Y, _viewport, height);

            if (_dragCurveHandle == null)
            {
                if (CurveSnapCheckBox.IsChecked == true && TryParseCurveNumber(CurveSnapStepTextBox.Text, out double step) && step > 0)
                {
                    x = Math.Round(x / step) * step;
                    y = Math.Round(y / step) * step;
                }
                List<CurveKey> others = CurveKeys.Where(k => !ReferenceEquals(k, _dragCurveKey)).OrderBy(k => k.X).ToList();
                double min = others.Where(k => k.X < _dragCurveKey.X).Select(k => k.X + 1e-6).DefaultIfEmpty(double.NegativeInfinity).Max();
                double max = others.Where(k => k.X > _dragCurveKey.X).Select(k => k.X - 1e-6).DefaultIfEmpty(double.PositiveInfinity).Min();
                _dragCurveKey.X = Math.Clamp(x, min, max);
                _dragCurveKey.Y = y;
            }
            else
            {
                double dx = x - _dragCurveKey.X;
                if (Math.Abs(dx) > 1e-9)
                {
                    double slope = (y - _dragCurveKey.Y) / dx;
                    _dragCurveKey.TangentMode = "Manual";
                    if (_dragCurveHandle == "in") _dragCurveKey.InTangent = slope;
                    else _dragCurveKey.OutTangent = slope;
                }
            }

            RenderCurveDesigner();
            return true;
        }

        private bool CurveDesignerMouseUp(MouseButtonEventArgs e)
        {
            if (_workspaceMode != WorkspaceMode.CurveDesigner || _dragCurveKey == null) return false;
            _dragCurveKey = null;
            _dragCurveHandle = null;
            PlotCanvas.ReleaseMouseCapture();
            RefreshCurveSuggestions();
            RenderCurveDesigner();
            e.Handled = true;
            return true;
        }

        private string? FindCurveHandleNear(Point point, CurveKey key, IReadOnlyList<CurveKey> ordered, double width, double height)
        {
            int index = IndexOfCurveKey(ordered, key);
            if (index < 0) return null;
            CurveResolvedKey resolved = CurveDesignerMath.ResolveTangents(ordered)[index];
            double span = _viewport.Width * 0.08;
            if (index > 0) span = Math.Min(span, (key.X - ordered[index - 1].X) * 0.35);
            if (index + 1 < ordered.Count) span = Math.Min(span, (ordered[index + 1].X - key.X) * 0.35);
            span = Math.Max(span, _viewport.Width * 0.015);
            Point pin = new(TransformX(key.X - span, _viewport, width), TransformY(key.Y - resolved.InSlope * span, _viewport, height));
            Point pout = new(TransformX(key.X + span, _viewport, width), TransformY(key.Y + resolved.OutSlope * span, _viewport, height));
            if ((pin - point).Length <= 11) return "in";
            if ((pout - point).Length <= 11) return "out";
            return null;
        }

        private static int IndexOfCurveKey(IReadOnlyList<CurveKey> keys, CurveKey key)
        {
            for (int i = 0; i < keys.Count; i++) if (ReferenceEquals(keys[i], key)) return i;
            return -1;
        }

        private double PixelDistance(Point p, CurveKey key, double width, double height)
        {
            double px = TransformX(key.X, _viewport, width);
            double py = TransformY(key.Y, _viewport, height);
            double dx = px - p.X, dy = py - p.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void DrawCurveRangeBoundary(double x, double width, double height)
        {
            double px = TransformX(x, _viewport, width);
            if (px < 0 || px > width) return;
            PlotCanvas.Children.Add(new Line
            {
                X1 = px, X2 = px, Y1 = 0, Y2 = height,
                Stroke = new SolidColorBrush(Color.FromArgb(70, 79, 92, 112)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                IsHitTestVisible = false
            });
        }

        private void DrawSelectedCurveSuggestion(IReadOnlyList<CurveKey> ordered, double width, double height)
        {
            if (CurveSuggestionsListBox?.SelectedItem is not CurveFunctionSuggestion suggestion) return;
            try
            {
                CalculatorEngine.CompiledExpression compiled = CalculatorEngine.Compile(suggestion.Formula);
                var points = new List<GraphPoint>();
                const int samples = 360;
                double minX = ordered[0].X;
                double maxX = ordered[^1].X;
                for (int i = 0; i <= samples; i++)
                {
                    double x = minX + (maxX - minX) * i / samples;
                    points.Add(new GraphPoint(x, compiled.Evaluate(x)));
                }

                Polyline line = new()
                {
                    Stroke = Brushes.DarkOrange,
                    StrokeThickness = 1.8,
                    StrokeDashArray = new DoubleCollection { 6, 4 },
                    StrokeLineJoin = PenLineJoin.Round,
                    Points = new PointCollection(),
                    IsHitTestVisible = false
                };
                foreach (GraphPoint point in points)
                {
                    if (!double.IsFinite(point.Y)) continue;
                    line.Points.Add(new Point(TransformX(point.X, _viewport, width), TransformY(point.Y, _viewport, height)));
                }
                PlotCanvas.Children.Add(line);
            }
            catch
            {
                // Suggestions are optional aids; a bad preview must never block curve editing.
            }
        }

        private static void SetCurveWrapCombo(ComboBox combo, CurveExtrapolationMode mode)
        {
            string tag = mode.ToString();
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private static CurveExtrapolationMode ReadCurveWrapMode(ComboBox combo)
        {
            string text = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? (combo.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? "Clamp";
            return Enum.TryParse(text.Replace(" ", string.Empty).Replace("+", ""), true, out CurveExtrapolationMode mode)
                ? mode
                : text.StartsWith("Repeat +", StringComparison.OrdinalIgnoreCase) ? CurveExtrapolationMode.RepeatOffset
                : text.StartsWith("Ping", StringComparison.OrdinalIgnoreCase) ? CurveExtrapolationMode.PingPong
                : text.StartsWith("Invert", StringComparison.OrdinalIgnoreCase) ? CurveExtrapolationMode.Invert
                : CurveExtrapolationMode.Clamp;
        }

        private static bool IsCyclingMode(CurveExtrapolationMode mode) => mode is
            CurveExtrapolationMode.Repeat or
            CurveExtrapolationMode.PingPong or
            CurveExtrapolationMode.Invert or
            CurveExtrapolationMode.RepeatOffset;

        private static string CurveModeLabel(CurveExtrapolationMode mode) => mode switch
        {
            CurveExtrapolationMode.RepeatOffset => "Repeat + offset",
            CurveExtrapolationMode.PingPong => "Ping-pong",
            CurveExtrapolationMode.Invert => "Invert repeat",
            _ => mode.ToString()
        };

        private static bool TryParseCurveNumber(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
