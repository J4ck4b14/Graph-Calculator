using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WpfTestApp
{
    public partial class MainWindow : Window
    {
        private readonly NumberFormatInfo _nfi = NumberFormatting.SpaceGroupedDot();
        private readonly List<(string expr, Brush color)> _functions = new();
        private readonly Brush[] _palette = new Brush[] { Brushes.Blue, Brushes.Red, Brushes.Green, Brushes.Orange, Brushes.Purple, Brushes.DarkCyan };

        public MainWindow()
        {
            InitializeComponent();
            ExpressionTextBox.KeyDown += ExpressionTextBox_KeyDown;
            PlotCanvas.MouseMove += PlotCanvas_MouseMove;
            PlotCanvas.MouseLeave += PlotCanvas_MouseLeave;
        }

        private void AddFunctionButton_Click(object sender, RoutedEventArgs e)
        {
            var expr = ExpressionTextBox.Text.Trim();
            if (string.IsNullOrEmpty(expr)) return;
            var color = _palette[_functions.Count % _palette.Length];
            _functions.Add((expr, color));
            FunctionsListBox.Items.Add(expr);
        }

        private void RemoveFunctionButton_Click(object sender, RoutedEventArgs e)
        {
            int i = FunctionsListBox.SelectedIndex;
            if (i >= 0 && i < _functions.Count)
            {
                _functions.RemoveAt(i);
                FunctionsListBox.Items.RemoveAt(i);
            }
        }

        private void ClearFunctionsButton_Click(object sender, RoutedEventArgs e)
        {
            _functions.Clear();
            FunctionsListBox.Items.Clear();
        }

        private void ExpressionTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                EvaluateExpression();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ClearAll();
                e.Handled = true;
            }
        }

        private void AppendButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Content is string s)
            {
                // Insert the button content at caret position
                int pos = ExpressionTextBox.CaretIndex;
                ExpressionTextBox.Text = ExpressionTextBox.Text.Insert(pos, s);
                ExpressionTextBox.CaretIndex = pos + s.Length;
                ExpressionTextBox.Focus();
            }
        }

        private void BackspaceButton_Click(object sender, RoutedEventArgs e)
        {
            int pos = ExpressionTextBox.CaretIndex;
            if (pos > 0)
            {
                ExpressionTextBox.Text = ExpressionTextBox.Text.Remove(pos - 1, 1);
                ExpressionTextBox.CaretIndex = pos - 1;
            }
            ExpressionTextBox.Focus();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearAll();
        }

        private void ClearAll()
        {
            ExpressionTextBox.Text = string.Empty;
            ResultTextBlock.Text = string.Empty;
            PlotCanvas.Children.Clear();
        }

        private void EvalButton_Click(object sender, RoutedEventArgs e)
        {
            EvaluateExpression();
        }

        private void EvaluateExpression()
        {
            var expr = ExpressionTextBox.Text;
            try
            {
                var val = CalculatorEngine.Evaluate(expr);
                ResultTextBlock.Text = FormatNumber(val);
            }
            catch (Exception ex)
            {
                ResultTextBlock.Text = "Error: " + ex.Message;
            }
        }

        private string FormatNumber(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsInfinity(v)) return v > 0 ? "Infinity" : "-Infinity";
            // format with up to 10 decimals, trim trailing zeros
            string s = v.ToString("N10", _nfi);
            if (s.Contains(_nfi.NumberDecimalSeparator))
            {
                s = s.TrimEnd('0');
                if (s.EndsWith(_nfi.NumberDecimalSeparator)) s = s.TrimEnd(_nfi.NumberDecimalSeparator.ToCharArray());
            }
            return s;
        }

        private async void PlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (_functions.Count == 0) { ResultTextBlock.Text = "No functions to plot"; return; }

            if (!double.TryParse(RangeStartTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var start))
            {
                ResultTextBlock.Text = "Invalid start"; return;
            }
            if (!double.TryParse(RangeEndTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
            {
                ResultTextBlock.Text = "Invalid end"; return;
            }
            if (end <= start) { ResultTextBlock.Text = "End must be > start"; return; }

            PlotCanvas.Children.Clear();

            // Run evaluation on background thread for each function
            var allPoints = await Task.Run(() =>
            {
                var list = new List<List<(double x, double y)>>();
                foreach (var f in _functions)
                {
                    list.Add(SampleFunction(f.expr, start, end, 800));
                }
                return list;
            });

            // Determine bounds across all functions
            var valid = allPoints.SelectMany(l => l).Where(p => !double.IsNaN(p.y) && !double.IsInfinity(p.y)).ToList();
            if (valid.Count == 0) { ResultTextBlock.Text = "No valid points to plot"; return; }
            double minX = valid.Min(p => p.x), maxX = valid.Max(p => p.x);
            double minY = valid.Min(p => p.y), maxY = valid.Max(p => p.y);
            if (maxY - minY < 1e-9) { maxY = minY + 1; minY = minY - 1; }

            double w = PlotCanvas.ActualWidth; if (double.IsNaN(w) || w == 0) w = PlotCanvas.Width;
            double h = PlotCanvas.ActualHeight; if (double.IsNaN(h) || h == 0) h = PlotCanvas.Height;

            // Draw axes
            DrawAxes(minX, maxX, minY, maxY, w, h);

            // Draw each function polyline
            for (int fi = 0; fi < _functions.Count; fi++)
            {
                var pts = allPoints[fi];
                Polyline current = null;
                double prevY = double.NaN;
                for (int i = 0; i < pts.Count; i++)
                {
                    var (x, y) = pts[i];
                    if (double.IsNaN(y) || double.IsInfinity(y)) { current = null; prevY = double.NaN; continue; }
                    double px = TransformX(x, minX, maxX, w);
                    double py = TransformY(y, minY, maxY, h);
                    if (double.IsNaN(prevY) || Math.Abs(py - prevY) > h * 0.5)
                    {
                        current = new Polyline { Stroke = _functions[fi].color, StrokeThickness = 1.5, Points = new PointCollection() };
                        PlotCanvas.Children.Add(current);
                        current.Points.Add(new System.Windows.Point(px, py));
                    }
                    else
                    {
                        current.Points.Add(new System.Windows.Point(px, py));
                    }
                    prevY = py;
                }
            }

            // store metadata for hover
            PlotCanvas.Tag = new PlotMetadata { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY };

            ResultTextBlock.Text = "Plotted " + _functions.Count + " function(s)";
        }

        private void PlotCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            HoverPanel.Visibility = Visibility.Collapsed;
        }

        private void PlotCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pt = e.GetPosition(PlotCanvas);
            if (PlotCanvas.ActualWidth == 0 || PlotCanvas.ActualHeight == 0) return;

            // We store last plotted extents in Tag of canvas
            if (PlotCanvas.Tag is not PlotMetadata meta) return;

            // Convert mouse X to function domain
            double x = meta.MinX + (pt.X / PlotCanvas.ActualWidth) * (meta.MaxX - meta.MinX);

            // Build hover info
            HoverXText.Text = "x = " + x.ToString("G6", CultureInfo.InvariantCulture);
            HoverValuesList.Items.Clear();

            int idx = 0;
            foreach (var f in _functions)
            {
                try
                {
                    var y = CalculatorEngine.Evaluate(f.expr, new Dictionary<string, double> { { "x", x } });
                    var tb = new TextBlock { Text = f.expr + " = " + FormatNumber(y), Foreground = f.color };
                    HoverValuesList.Items.Add(tb);
                }
                catch
                {
                    var tb = new TextBlock { Text = f.expr + " = " + "NaN", Foreground = f.color };
                    HoverValuesList.Items.Add(tb);
                }
                idx++;
            }

            // Position hover panel near mouse, keep inside canvas bounds
            double left = pt.X + 12;
            double top = pt.Y + 12;
            // measure panel size if needed
            HoverPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var panelW = HoverPanel.DesiredSize.Width;
            var panelH = HoverPanel.DesiredSize.Height;
            if (left + panelW > PlotCanvas.ActualWidth) left = PlotCanvas.ActualWidth - panelW - 4;
            if (top + panelH > PlotCanvas.ActualHeight) top = PlotCanvas.ActualHeight - panelH - 4;
            if (left < 0) left = 0; if (top < 0) top = 0;

            Canvas.SetLeft(HoverPanel, left);
            Canvas.SetTop(HoverPanel, top);
            HoverPanel.Visibility = Visibility.Visible;
        }

        private void DrawAxes(double minX, double maxX, double minY, double maxY, double w, double h)
        {
            // Axis brushes
            var axisBrush = Brushes.Black;
            var tickBrush = Brushes.Gray;
            var labelBrush = Brushes.Black;

            // Determine if zero is inside range
            bool drawYAxisAtZero = minX <= 0 && maxX >= 0;
            bool drawXAxisAtZero = minY <= 0 && maxY >= 0;

            double x0 = TransformX(0, minX, maxX, w); // position where x==0 maps to
            double y0 = TransformY(0, minY, maxY, h);

            // If zero not in range, draw axes at frame edges (left/bottom)
            double yAxisX = drawYAxisAtZero ? x0 : 0; // left edge if not in range
            double xAxisY = drawXAxisAtZero ? y0 : h; // bottom edge if not in range

            // Draw vertical axis
            var vline = new Line { X1 = yAxisX, X2 = yAxisX, Y1 = 0, Y2 = h, Stroke = axisBrush, StrokeThickness = 1 };
            PlotCanvas.Children.Add(vline);

            // Draw horizontal axis
            var hline = new Line { X1 = 0, X2 = w, Y1 = xAxisY, Y2 = xAxisY, Stroke = axisBrush, StrokeThickness = 1 };
            PlotCanvas.Children.Add(hline);

            // Compute ticks for X and Y
            var xticks = NiceTicks(minX, maxX, 8);
            var yticks = NiceTicks(minY, maxY, 6);

            // Draw X ticks and labels
            foreach (var tx in xticks)
            {
                double px = TransformX(tx, minX, maxX, w);
                var tline = new Line { X1 = px, X2 = px, Y1 = xAxisY - 4, Y2 = xAxisY + 4, Stroke = tickBrush, StrokeThickness = 1 };
                PlotCanvas.Children.Add(tline);
                var label = new TextBlock { Text = tx.ToString("G5", CultureInfo.InvariantCulture), Foreground = labelBrush, FontSize = 12 };
                Canvas.SetLeft(label, px + 4);
                Canvas.SetTop(label, xAxisY + 6);
                PlotCanvas.Children.Add(label);
            }

            // Draw Y ticks and labels
            foreach (var ty in yticks)
            {
                double py = TransformY(ty, minY, maxY, h);
                var tline = new Line { X1 = yAxisX - 4, X2 = yAxisX + 4, Y1 = py, Y2 = py, Stroke = tickBrush, StrokeThickness = 1 };
                PlotCanvas.Children.Add(tline);
                var label = new TextBlock { Text = ty.ToString("G5", CultureInfo.InvariantCulture), Foreground = labelBrush, FontSize = 12 };
                // place label to left of Y axis if axis near right, otherwise to left
                double left = yAxisX + 6;
                if (yAxisX < 40) left = 4; // small margin
                Canvas.SetLeft(label, left);
                Canvas.SetTop(label, py - 10);
                PlotCanvas.Children.Add(label);
            }
        }

        // Compute a list of "nice" tick values between min and max, aiming for approx count ticks
        private static List<double> NiceTicks(double min, double max, int approxCount)
        {
            var ticks = new List<double>();
            double range = NiceNumber(max - min, false);
            double tickSpacing = NiceNumber(range / (approxCount - 1), true);
            double niceMin = Math.Floor(min / tickSpacing) * tickSpacing;
            double niceMax = Math.Ceiling(max / tickSpacing) * tickSpacing;
            for (double val = niceMin; val <= niceMax + 0.5 * tickSpacing; val += tickSpacing)
            {
                // avoid floating point accumulation error by rounding
                double r = Math.Round(val, 12);
                if (r >= min - 1e-12 && r <= max + 1e-12) ticks.Add(r);
            }
            return ticks;
        }

        // Helper to compute "nice" numbers for axis ticks (from Graphics Gems / common practice)
        private static double NiceNumber(double range, bool round)
        {
            double exponent = Math.Floor(Math.Log10(range));
            double fraction = range / Math.Pow(10, exponent);
            double niceFraction;
            if (round)
            {
                if (fraction < 1.5) niceFraction = 1;
                else if (fraction < 3) niceFraction = 2;
                else if (fraction < 7) niceFraction = 5;
                else niceFraction = 10;
            }
            else
            {
                if (fraction <= 1) niceFraction = 1;
                else if (fraction <= 2) niceFraction = 2;
                else if (fraction <= 5) niceFraction = 5;
                else niceFraction = 10;
            }
            return niceFraction * Math.Pow(10, exponent);
        }

        private static double TransformX(double x, double minX, double maxX, double width)
        {
            return (x - minX) / (maxX - minX) * width;
        }
        private static double TransformY(double y, double minY, double maxY, double height)
        {
            // invert Y for screen coordinates
            return height - (y - minY) / (maxY - minY) * height;
        }

        private static List<(double x, double y)> SampleFunction(string expr, double start, double end, int samples)
        {
            var res = new List<(double x, double y)>();
            for (int i = 0; i < samples; i++)
            {
                double x = start + (end - start) * i / (samples - 1);
                try
                {
                    var val = CalculatorEngine.Evaluate(expr, new Dictionary<string, double> { { "x", x } });
                    res.Add((x, val));
                }
                catch
                {
                    res.Add((x, double.NaN));
                }
            }
            return res;
        }
    }

    internal class PlotMetadata
    {
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
    }
}