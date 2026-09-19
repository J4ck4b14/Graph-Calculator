using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GraphCalculator
{
    public partial class MainWindow : Window
    {
        private readonly Brush[] _palette =
        {
            Brushes.RoyalBlue,
            Brushes.Crimson,
            Brushes.SeaGreen,
            Brushes.DarkOrange,
            Brushes.MediumPurple,
            Brushes.DarkCyan,
            Brushes.DeepPink,
            Brushes.SaddleBrown
        };

        private readonly DispatcherTimer _renderTimer;
        private CancellationTokenSource? _renderCancellation;
        private CancellationTokenSource? _fitCancellation;
        private PlotViewport _viewport = new(-10, 10, -10, 10);
        private TextBox? _activeExpressionBox;
        private int _nextColorIndex;
        private bool _isDragging;
        private Point _dragStart;
        private PlotViewport _dragStartViewport;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _renderTimer.Tick += RenderTimer_Tick;
        }

        public ObservableCollection<GraphExpression> Expressions { get; } = new();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Expressions.Count == 0)
            {
                AddExpression();
            }

            UpdateViewRangeText();
            ScheduleRender(10);

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                FocusExpression(Expressions.FirstOrDefault());
            }));
        }

        protected override void OnClosed(EventArgs e)
        {
            _renderCancellation?.Cancel();
            _fitCancellation?.Cancel();
            base.OnClosed(e);
        }

        private GraphExpression AddExpression(string expression = "")
        {
            var item = new GraphExpression(_palette[_nextColorIndex++ % _palette.Length])
            {
                Expression = expression
            };

            item.PropertyChanged += Expression_PropertyChanged;
            Expressions.Add(item);
            RefreshExpression(item);
            return item;
        }

        private void AddExpressionButton_Click(object sender, RoutedEventArgs e)
        {
            var item = AddExpression();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusExpression(item)));
        }

        private void RemoveExpressionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: GraphExpression item }) return;

            item.PropertyChanged -= Expression_PropertyChanged;
            Expressions.Remove(item);

            if (_activeExpressionBox?.DataContext == item)
            {
                _activeExpressionBox = null;
            }

            if (Expressions.Count == 0)
            {
                AddExpression();
            }

            ScheduleRender(20);
        }

        private void Expression_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not GraphExpression item) return;

            if (e.PropertyName == nameof(GraphExpression.Expression))
            {
                RefreshExpression(item);
                ScheduleRender(180);
            }
            else if (e.PropertyName == nameof(GraphExpression.IsVisible))
            {
                ScheduleRender(30);
            }
        }

        private static void RefreshExpression(GraphExpression item)
        {
            if (string.IsNullOrWhiteSpace(item.Expression))
            {
                item.Compiled = null;
                item.StatusText = string.Empty;
                return;
            }

            try
            {
                item.Compiled = CalculatorEngine.Compile(item.Expression);
                item.StatusText = item.Compiled.DependsOnX
                    ? string.Empty
                    : "= " + NumberFormatting.Format(item.Compiled.Evaluate());
            }
            catch (Exception ex)
            {
                item.Compiled = null;
                item.StatusText = "Error: " + ex.Message;
            }
        }

        private void ExpressionBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox box)
            {
                _activeExpressionBox = box;
            }
        }

        private void ExpressionBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox box) return;

            if (e.Key == Key.Enter)
            {
                if (box.DataContext is GraphExpression item)
                {
                    RefreshExpression(item);
                }

                ScheduleRender(0);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Clear();
                e.Handled = true;
            }
        }

        private void KeypadButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string text })
            {
                InsertIntoActiveExpression(text);
            }
        }

        private void BackspaceButton_Click(object sender, RoutedEventArgs e)
        {
            TextBox? box = GetActiveExpressionBox();
            if (box == null) return;

            if (box.SelectionLength > 0)
            {
                int start = box.SelectionStart;
                box.Text = box.Text.Remove(start, box.SelectionLength);
                box.CaretIndex = start;
            }
            else if (box.CaretIndex > 0)
            {
                int position = box.CaretIndex;
                box.Text = box.Text.Remove(position - 1, 1);
                box.CaretIndex = position - 1;
            }

            box.Focus();
        }

        private void InsertIntoActiveExpression(string text)
        {
            TextBox? box = GetActiveExpressionBox();
            if (box == null)
            {
                if (Expressions.Count == 0) AddExpression();
                Expressions[0].Expression += text;
                FocusExpression(Expressions[0], moveCaretToEnd: true);
                return;
            }

            int start = box.SelectionStart;
            string value = box.Text;

            if (box.SelectionLength > 0)
            {
                value = value.Remove(start, box.SelectionLength);
            }

            box.Text = value.Insert(start, text);
            box.CaretIndex = start + text.Length;
            box.Focus();
        }

        private TextBox? GetActiveExpressionBox()
        {
            if (_activeExpressionBox?.IsLoaded == true) return _activeExpressionBox;

            FocusExpression(Expressions.FirstOrDefault());
            return _activeExpressionBox;
        }

        private void FocusExpression(GraphExpression? item, bool moveCaretToEnd = false)
        {
            if (item == null) return;

            ExpressionsListBox.ScrollIntoView(item);
            ExpressionsListBox.UpdateLayout();
            if (ExpressionsListBox.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return;

            TextBox? box = FindVisualChild<TextBox>(container);
            if (box == null) return;

            _activeExpressionBox = box;
            box.Focus();
            if (moveCaretToEnd)
            {
                box.CaretIndex = box.Text.Length;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;

                T? nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }

            return null;
        }

        private void RenderTimer_Tick(object? sender, EventArgs e)
        {
            _renderTimer.Stop();
            _ = RenderGraphAsync();
        }

        private void ScheduleRender(int delayMilliseconds)
        {
            if (!IsLoaded) return;

            _renderTimer.Stop();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMilliseconds));
            _renderTimer.Start();
        }

        private async Task RenderGraphAsync()
        {
            double width = GraphSurface.ActualWidth;
            double height = GraphSurface.ActualHeight;
            if (width < 20 || height < 20) return;

            _renderCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _renderCancellation = cancellation;
            CancellationToken token = cancellation.Token;

            var active = Expressions
                .Where(item => item.IsVisible && item.Compiled != null && !string.IsNullOrWhiteSpace(item.Expression))
                .Select(item => new SeriesRequest(item, item.Compiled!))
                .ToList();

            PlotViewport viewport = _viewport;
            int pixelWidth = Math.Max(1, (int)Math.Round(width));
            int pixelHeight = Math.Max(1, (int)Math.Round(height));

            try
            {
                List<List<GraphPoint>> samples = await Task.Run(() =>
                {
                    var result = new List<List<GraphPoint>>(active.Count);
                    foreach (SeriesRequest request in active)
                    {
                        token.ThrowIfCancellationRequested();
                        result.Add(GraphSampler.Sample(request.Compiled, viewport, pixelWidth, pixelHeight, token));
                    }

                    return result;
                }, token);

                token.ThrowIfCancellationRequested();

                PlotCanvas.Children.Clear();
                DrawGridAndAxes(viewport, width, height);

                for (int i = 0; i < active.Count; i++)
                {
                    DrawSeries(samples[i], active[i].Expression.Color, viewport, width, height);
                }

                GraphStatusText.Text = active.Count switch
                {
                    0 => "No visible expressions",
                    1 => "1 expression",
                    _ => $"{active.Count} expressions"
                };

                UpdateViewRangeText();
            }
            catch (OperationCanceledException)
            {
                // A newer render request has already replaced this one.
            }
            finally
            {
                if (ReferenceEquals(_renderCancellation, cancellation))
                {
                    _renderCancellation = null;
                }

                cancellation.Dispose();
            }
        }

        private void DrawGridAndAxes(PlotViewport viewport, double width, double height)
        {
            var gridBrush = new SolidColorBrush(Color.FromRgb(232, 235, 240));
            var axisBrush = new SolidColorBrush(Color.FromRgb(128, 136, 148));
            var labelBrush = new SolidColorBrush(Color.FromRgb(91, 99, 111));

            int xTarget = Math.Clamp((int)(width / 105), 4, 12);
            int yTarget = Math.Clamp((int)(height / 85), 4, 10);
            List<double> xTicks = NiceTicks(viewport.MinX, viewport.MaxX, xTarget);
            List<double> yTicks = NiceTicks(viewport.MinY, viewport.MaxY, yTarget);

            foreach (double x in xTicks)
            {
                double px = TransformX(x, viewport, width);
                PlotCanvas.Children.Add(new Line
                {
                    X1 = px,
                    X2 = px,
                    Y1 = 0,
                    Y2 = height,
                    Stroke = gridBrush,
                    StrokeThickness = 1
                });

                var label = new TextBlock
                {
                    Text = NumberFormatting.FormatAxis(x),
                    Foreground = labelBrush,
                    FontSize = 11
                };
                Canvas.SetLeft(label, px + 4);
                Canvas.SetTop(label, height - 20);
                PlotCanvas.Children.Add(label);
            }

            foreach (double y in yTicks)
            {
                double py = TransformY(y, viewport, height);
                PlotCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = py,
                    Y2 = py,
                    Stroke = gridBrush,
                    StrokeThickness = 1
                });

                var label = new TextBlock
                {
                    Text = NumberFormatting.FormatAxis(y),
                    Foreground = labelBrush,
                    FontSize = 11
                };
                Canvas.SetLeft(label, 5);
                Canvas.SetTop(label, py - 16);
                PlotCanvas.Children.Add(label);
            }

            if (viewport.MinX <= 0 && viewport.MaxX >= 0)
            {
                double x0 = TransformX(0, viewport, width);
                PlotCanvas.Children.Add(new Line
                {
                    X1 = x0,
                    X2 = x0,
                    Y1 = 0,
                    Y2 = height,
                    Stroke = axisBrush,
                    StrokeThickness = 1.4
                });
            }

            if (viewport.MinY <= 0 && viewport.MaxY >= 0)
            {
                double y0 = TransformY(0, viewport, height);
                PlotCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = y0,
                    Y2 = y0,
                    Stroke = axisBrush,
                    StrokeThickness = 1.4
                });
            }
        }

        private void DrawSeries(
            IReadOnlyList<GraphPoint> points,
            Brush color,
            PlotViewport viewport,
            double width,
            double height)
        {
            Polyline? line = null;
            double previousPixelY = double.NaN;
            double previousY = double.NaN;

            foreach (GraphPoint point in points)
            {
                if (!double.IsFinite(point.Y))
                {
                    line = null;
                    previousPixelY = double.NaN;
                    previousY = double.NaN;
                    continue;
                }

                double px = TransformX(point.X, viewport, width);
                double py = TransformY(point.Y, viewport, height);

                bool likelyJump = double.IsFinite(previousPixelY)
                    && Math.Abs(py - previousPixelY) > height * 0.7
                    && Math.Abs(point.Y - previousY) > viewport.Height * 0.7;

                if (line == null || likelyJump)
                {
                    line = new Polyline
                    {
                        Stroke = color,
                        StrokeThickness = 2,
                        StrokeLineJoin = PenLineJoin.Round,
                        Points = new PointCollection()
                    };
                    PlotCanvas.Children.Add(line);
                }

                line.Points.Add(new Point(px, py));
                previousPixelY = py;
                previousY = point.Y;
            }
        }

        private async void FitGraphButton_Click(object sender, RoutedEventArgs e)
        {
            var compiled = Expressions
                .Where(item => item.IsVisible && item.Compiled != null)
                .Select(item => item.Compiled!)
                .ToList();

            if (compiled.Count == 0)
            {
                GraphStatusText.Text = "Nothing to fit";
                return;
            }

            _fitCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _fitCancellation = cancellation;
            GraphStatusText.Text = "Fitting…";

            try
            {
                var bounds = await Task.Run(
                    () => GraphSampler.FindYBounds(compiled, _viewport.MinX, _viewport.MaxX, cancellation.Token),
                    cancellation.Token);

                if (!bounds.HasValue)
                {
                    GraphStatusText.Text = "No finite values in view";
                    return;
                }

                _viewport = _viewport.WithY(bounds.Value.minY, bounds.Value.maxY);
                UpdateViewRangeText();
                ScheduleRender(0);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_fitCancellation, cancellation))
                {
                    _fitCancellation = null;
                }

                cancellation.Dispose();
            }
        }

        private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            _viewport = new PlotViewport(-10, 10, -10, 10);
            UpdateViewRangeText();
            ScheduleRender(0);
        }

        private void GraphSurface_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateOverlaySize();
            ScheduleRender(100);
        }

        private void PlotCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PlotCanvas.ActualWidth <= 0 || PlotCanvas.ActualHeight <= 0) return;

            Point point = e.GetPosition(PlotCanvas);
            double anchorX = PixelToX(point.X, _viewport, PlotCanvas.ActualWidth);
            double anchorY = PixelToY(point.Y, _viewport, PlotCanvas.ActualHeight);
            double factor = e.Delta > 0 ? 0.82 : 1.22;

            _viewport = _viewport.Zoom(factor, anchorX, anchorY);
            UpdateViewRangeText();
            ScheduleRender(25);
            UpdateHover(point);
            e.Handled = true;
        }

        private void PlotCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(PlotCanvas);
            _dragStartViewport = _viewport;
            PlotCanvas.CaptureMouse();
            PlotCanvas.Cursor = Cursors.SizeAll;
            HideHover();
            e.Handled = true;
        }

        private void PlotCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            PlotCanvas.ReleaseMouseCapture();
            PlotCanvas.Cursor = Cursors.Arrow;
            ScheduleRender(0);
            UpdateHover(e.GetPosition(PlotCanvas));
            e.Handled = true;
        }

        private void PlotCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point point = e.GetPosition(PlotCanvas);

            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                double width = PlotCanvas.ActualWidth;
                double height = PlotCanvas.ActualHeight;
                if (width <= 0 || height <= 0) return;

                double dx = point.X - _dragStart.X;
                double dy = point.Y - _dragStart.Y;
                double xOffset = -dx / width * _dragStartViewport.Width;
                double yOffset = dy / height * _dragStartViewport.Height;

                _viewport = _dragStartViewport.Pan(xOffset, yOffset);
                UpdateViewRangeText();
                ScheduleRender(35);
                return;
            }

            if (_isDragging && e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                PlotCanvas.ReleaseMouseCapture();
                PlotCanvas.Cursor = Cursors.Arrow;
            }

            UpdateHover(point);
        }

        private void PlotCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                HideHover();
            }
        }

        private void UpdateHover(Point point)
        {
            double width = PlotCanvas.ActualWidth;
            double height = PlotCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            if (point.X < 0 || point.Y < 0 || point.X > width || point.Y > height) return;

            double x = PixelToX(point.X, _viewport, width);
            double y = PixelToY(point.Y, _viewport, height);

            CrosshairVertical.X1 = point.X;
            CrosshairVertical.X2 = point.X;
            CrosshairVertical.Y1 = 0;
            CrosshairVertical.Y2 = height;
            CrosshairVertical.Visibility = Visibility.Visible;

            CrosshairHorizontal.X1 = 0;
            CrosshairHorizontal.X2 = width;
            CrosshairHorizontal.Y1 = point.Y;
            CrosshairHorizontal.Y2 = point.Y;
            CrosshairHorizontal.Visibility = Visibility.Visible;

            HoverXText.Text = $"x = {NumberFormatting.Format(x)}   y = {NumberFormatting.Format(y)}";
            HoverValuesList.Items.Clear();

            foreach (GraphExpression item in Expressions.Where(item => item.IsVisible && item.Compiled != null))
            {
                double value;
                try
                {
                    value = item.Compiled!.Evaluate(x);
                }
                catch
                {
                    value = double.NaN;
                }

                HoverValuesList.Items.Add(new TextBlock
                {
                    Text = $"{item.Expression} = {NumberFormatting.Format(value)}",
                    Foreground = item.Color,
                    FontSize = 12,
                    MaxWidth = 320,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            HoverPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double panelWidth = HoverPanel.DesiredSize.Width;
            double panelHeight = HoverPanel.DesiredSize.Height;
            double left = point.X + 14;
            double top = point.Y + 14;

            if (left + panelWidth > width) left = point.X - panelWidth - 14;
            if (top + panelHeight > height) top = point.Y - panelHeight - 14;

            Canvas.SetLeft(HoverPanel, Math.Max(4, left));
            Canvas.SetTop(HoverPanel, Math.Max(4, top));
            HoverPanel.Visibility = Visibility.Visible;
        }

        private void HideHover()
        {
            HoverPanel.Visibility = Visibility.Collapsed;
            CrosshairVertical.Visibility = Visibility.Collapsed;
            CrosshairHorizontal.Visibility = Visibility.Collapsed;
        }

        private void UpdateOverlaySize()
        {
            OverlayCanvas.Width = GraphSurface.ActualWidth;
            OverlayCanvas.Height = GraphSurface.ActualHeight;
        }

        private void UpdateViewRangeText()
        {
            ViewRangeText.Text =
                $"x: {NumberFormatting.FormatAxis(_viewport.MinX)} to {NumberFormatting.FormatAxis(_viewport.MaxX)}   " +
                $"y: {NumberFormatting.FormatAxis(_viewport.MinY)} to {NumberFormatting.FormatAxis(_viewport.MaxY)}";
        }

        private static double TransformX(double x, PlotViewport viewport, double width)
        {
            return (x - viewport.MinX) / viewport.Width * width;
        }

        private static double TransformY(double y, PlotViewport viewport, double height)
        {
            return height - (y - viewport.MinY) / viewport.Height * height;
        }

        private static double PixelToX(double x, PlotViewport viewport, double width)
        {
            return viewport.MinX + x / width * viewport.Width;
        }

        private static double PixelToY(double y, PlotViewport viewport, double height)
        {
            return viewport.MaxY - y / height * viewport.Height;
        }

        private static List<double> NiceTicks(double min, double max, int targetCount)
        {
            var ticks = new List<double>();
            double span = max - min;
            if (!double.IsFinite(span) || span <= 0) return ticks;

            double roughStep = span / Math.Max(2, targetCount);
            double step = NiceNumber(roughStep);
            double first = Math.Ceiling(min / step) * step;

            for (double value = first; value <= max + step * 0.25; value += step)
            {
                ticks.Add(Math.Abs(value) < step * 1e-10 ? 0 : value);
                if (ticks.Count > 100) break;
            }

            return ticks;
        }

        // Keeps the grid intervals readable as the viewport changes scale.
        private static double NiceNumber(double value)
        {
            double exponent = Math.Floor(Math.Log10(value));
            double fraction = value / Math.Pow(10, exponent);
            double niceFraction = fraction switch
            {
                < 1.5 => 1,
                < 3 => 2,
                < 7 => 5,
                _ => 10
            };

            return niceFraction * Math.Pow(10, exponent);
        }

        private sealed record SeriesRequest(
            GraphExpression Expression,
            CalculatorEngine.CompiledExpression Compiled);
    }
}
