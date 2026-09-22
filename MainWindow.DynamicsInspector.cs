using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private GraphExpression? _dynamicsInspectorExpression;
        private IReadOnlyList<DynamicalSystemSampler.Sample> _dynamicsInspectorSamples = Array.Empty<DynamicalSystemSampler.Sample>();
        private string[] _dynamicsStateNames = Array.Empty<string>();
        private string _dynamicsInspectorSignature = string.Empty;
        private double? _dynamicsSelectedTime;
        private readonly List<double> _dynamicsTableTimes = new();
        private bool _darkTheme = true;

        private void InitializeDynamicsInspectorAndTheme()
        {
            ApplyTheme(dark: true, updateButton: true);
            UpdateDynamicsInspector(force: true);
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(!_darkTheme, updateButton: true);
        }

        private void ApplyTheme(bool dark, bool updateButton)
        {
            _darkTheme = dark;
            if (updateButton && ThemeToggleButton != null)
            {
                ThemeToggleButton.Content = dark ? "☀" : "☾";
                ThemeToggleButton.ToolTip = dark ? "Switch to light theme" : "Switch to dark theme";
            }

            SetThemeBrush("AppBackgroundBrush", dark ? "#FF15181D" : "#FFF4F5F7");
            SetThemeBrush("PanelBackgroundBrush", dark ? "#FF1D2128" : "#FFFFFFFF");
            SetThemeBrush("SubtleBackgroundBrush", dark ? "#FF242933" : "#FFF8F9FB");
            SetThemeBrush("GraphBackgroundBrush", dark ? "#FF171B21" : "#FFFBFCFE");
            SetThemeBrush("InputBackgroundBrush", dark ? "#FF20252D" : "#FFFFFFFF");
            SetThemeBrush("TextBrush", dark ? "#FFF2F4F7" : "#FF15181D");
            SetThemeBrush("MutedTextBrush", dark ? "#FFAAB2BF" : "#FF6B7280");
            SetThemeBrush("BorderBrush", dark ? "#FF343B46" : "#FFE1E3E7");
            SetThemeBrush("OverlayBackgroundBrush", dark ? "#F21D2128" : "#F2FFFFFF");
            SetThemeBrush("GridLineBrush", dark ? "#FF2B313B" : "#FFE8EBF0");
            SetThemeBrush("AxisBrush", dark ? "#FF7F8997" : "#FF808894");
            SetThemeBrush("HoverBackgroundBrush", dark ? "#FF2C333E" : "#FFE9EDF3");
            SetThemeBrush("SelectedBackgroundBrush", dark ? "#FF35445F" : "#FFDCE8FA");
            SetThemeBrush("DisabledBackgroundBrush", dark ? "#FF1A1E24" : "#FFF0F1F3");
            SetThemeBrush("DisabledTextBrush", dark ? "#FF707987" : "#FF9AA0AA");

            // Some stock WPF templates still read SystemColors, so override those brushes too.
            SetSystemThemeBrush(SystemColors.WindowBrushKey, dark ? "#FF20252D" : "#FFFFFFFF");
            SetSystemThemeBrush(SystemColors.WindowTextBrushKey, dark ? "#FFF2F4F7" : "#FF15181D");
            SetSystemThemeBrush(SystemColors.ControlBrushKey, dark ? "#FF1D2128" : "#FFF4F5F7");
            SetSystemThemeBrush(SystemColors.ControlTextBrushKey, dark ? "#FFF2F4F7" : "#FF15181D");
            SetSystemThemeBrush(SystemColors.HighlightBrushKey, dark ? "#FF35445F" : "#FFDCE8FA");
            SetSystemThemeBrush(SystemColors.HighlightTextBrushKey, dark ? "#FFFFFFFF" : "#FF111827");
            SetSystemThemeBrush(SystemColors.GrayTextBrushKey, dark ? "#FF707987" : "#FF9AA0AA");
            SetSystemThemeBrush(SystemColors.MenuBrushKey, dark ? "#FF1D2128" : "#FFFFFFFF");
            SetSystemThemeBrush(SystemColors.MenuTextBrushKey, dark ? "#FFF2F4F7" : "#FF15181D");

            TrySetImmersiveDarkTitleBar(dark);
            RedrawDynamicsTimeSeries();
            if (IsLoaded) ScheduleRender(0);
        }

        private void SetThemeBrush(string key, string color)
        {
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        }

        private void SetSystemThemeBrush(object key, string color)
        {
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        }

        private Brush ThemeBrush(string key, Brush fallback)
            => Resources[key] as Brush ?? fallback;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void TrySetImmersiveDarkTitleBar(bool dark)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int value = dark ? 1 : 0;
                int size = sizeof(int);
                if (DwmSetWindowAttribute(hwnd, 20, ref value, size) != 0)
                    _ = DwmSetWindowAttribute(hwnd, 19, ref value, size);
            }
            catch
            {
                // Theme remains usable even on Windows versions without this DWM attribute.
            }
        }

        private void GraphToolsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || GraphToolsTabs == null) return;

            if (ReferenceEquals(GraphToolsTabs.SelectedItem, DynamicsInspectorPanel))
                RedrawDynamicsTimeSeries();

            // Re-render after the tray resizes the graph area.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsLoaded) ScheduleRender(0);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void DynamicsInspectorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || DynamicsInspectorTabs == null) return;
            if (DynamicsInspectorTabs.SelectedIndex == 0) RedrawDynamicsTimeSeries();
        }

        private void EnsureGraphToolsTabSelection()
        {
            if (GraphToolsTabs == null || GraphToolsTabs.Visibility != Visibility.Visible) return;

            if (GraphToolsTabs.SelectedItem is TabItem selected && selected.Visibility == Visibility.Visible)
                return;

            TabItem? fallback = new[] { DynamicsInspectorPanel, PlotRangePanel, SurfaceRangePanel, TimelinePanel, AnalysisToolsPanel }
                .FirstOrDefault(tab => tab != null && tab.Visibility == Visibility.Visible);
            if (fallback != null) GraphToolsTabs.SelectedItem = fallback;
        }

        private void DynamicsVisualOption_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) ScheduleRender(0);
        }

        private void DynamicsTableOption_Changed(object sender, SelectionChangedEventArgs e)
        {
            RebuildDynamicsTable();
        }

        private void DynamicsTableOption_Changed(object sender, RoutedEventArgs e)
        {
            RebuildDynamicsTable();
        }

        private void DynamicsTimeSeriesCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawDynamicsTimeSeries();
        }

        private void DynamicsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int index = DynamicsDataGrid?.SelectedIndex ?? -1;
            if (index >= 0 && index < _dynamicsTableTimes.Count)
            {
                _dynamicsSelectedTime = _dynamicsTableTimes[index];
                _timelineTime = Math.Clamp(_dynamicsSelectedTime.Value, _timelineStart, _timelineEnd);
                UpdateTimelineUi();
                UpdateDynamicsCurrentStateText();
                if (IsLoaded) ScheduleRender(0);
            }
        }

        private GraphExpression? GetPrimaryDynamicsExpression()
        {
            if (_workspaceMode != WorkspaceMode.FunctionLab) return null;
            return Expressions.FirstOrDefault(item => item.IsVisible && item.CompiledParts.Any() && item.Kind switch
            {
                GraphExpressionKind.DifferentialEquation1D => !_is3DMode,
                GraphExpressionKind.DynamicalSystem2D => !_is3DMode,
                GraphExpressionKind.DynamicalSystem3D => _is3DMode,
                _ => false
            });
        }

        private void UpdateDynamicsInspector(bool force = false)
        {
            if (DynamicsInspectorPanel == null) return;

            GraphExpression? expression = GetPrimaryDynamicsExpression();
            bool inspectorWasVisible = DynamicsInspectorPanel.Visibility == Visibility.Visible;
            if (expression == null)
            {
                DynamicsInspectorPanel.Visibility = Visibility.Collapsed;
                EnsureGraphToolsTabSelection();
                _dynamicsInspectorExpression = null;
                _dynamicsInspectorSamples = Array.Empty<DynamicalSystemSampler.Sample>();
                _dynamicsStateNames = Array.Empty<string>();
                _dynamicsInspectorSignature = string.Empty;
                return;
            }

            DynamicsInspectorPanel.Visibility = Visibility.Visible;
            if (!inspectorWasVisible) EnsureGraphToolsTabSelection();
            int dimensions = GetDynamicsDimensions(expression.Kind);
            if (dimensions == 0) return;

            Dictionary<string, double> variables = GetParameterValues();
            if (!TryReadExpressionDomain(expression, variables, out ExpressionDomain domain, out _, includeYOverride: false)) return;

            string[] names = ExtractDynamicsStateNames(expression.Expression, dimensions);
            string signature = expression.Expression + "|" + dimensions + "|" +
                (domain.MinX?.ToString("R", CultureInfo.InvariantCulture) ?? "") + "|" +
                (domain.MaxX?.ToString("R", CultureInfo.InvariantCulture) ?? "") + "|" +
                string.Join(";", variables
                    .Where(pair => !pair.Key.Equals("t", StringComparison.OrdinalIgnoreCase) && !pair.Key.Equals("time", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(pair => pair.Key)
                    .Select(pair => pair.Key + "=" + pair.Value.ToString("R", CultureInfo.InvariantCulture)));

            if (force || signature != _dynamicsInspectorSignature)
            {
                _dynamicsInspectorSignature = signature;
                _dynamicsInspectorExpression = expression;
                _dynamicsStateNames = names;
                _dynamicsInspectorSamples = DynamicalSystemSampler.SampleStateData(
                    expression.Components,
                    dimensions,
                    variables,
                    domain.MinX,
                    domain.MaxX,
                    520,
                    CancellationToken.None);
                if (domain.MinX.HasValue && domain.MaxX.HasValue && domain.MaxX.Value > domain.MinX.Value)
                {
                    _timelineStart = domain.MinX.Value;
                    _timelineEnd = domain.MaxX.Value;
                    _timelineTime = Math.Clamp(_timelineTime, _timelineStart, _timelineEnd);
                    UpdateTimelineUi();
                }
                _dynamicsSelectedTime = null;
                RebuildDynamicsTable();
                RedrawDynamicsTimeSeries();
            }

            UpdateDynamicsSummary(expression, dimensions, names, domain);
            UpdateDynamicsCurrentStateText();
        }

        private static int GetDynamicsDimensions(GraphExpressionKind kind) => kind switch
        {
            GraphExpressionKind.DifferentialEquation1D => 1,
            GraphExpressionKind.DynamicalSystem2D => 2,
            GraphExpressionKind.DynamicalSystem3D => 3,
            _ => 0
        };

        private static string[] ExtractDynamicsStateNames(string expression, int dimensions)
        {
            var names = new List<string>();
            foreach (Match match in Regex.Matches(expression,
                @"(?im)^\s*d\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*/\s*d\s*t\s*=|^\s*(?<prime>[A-Za-z_][A-Za-z0-9_]*)\s*'\s*="))
            {
                string value = match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups["prime"].Value;
                if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value, StringComparer.OrdinalIgnoreCase)) names.Add(value);
            }

            string[] fallback = ["x", "y", "z"];
            while (names.Count < dimensions) names.Add(fallback[names.Count]);
            return names.Take(dimensions).ToArray();
        }

        private void UpdateDynamicsSummary(GraphExpression expression, int dimensions, string[] names, ExpressionDomain domain)
        {
            string start = domain.MinX.HasValue ? NumberFormatting.Format(domain.MinX.Value) : "t₀";
            string end = domain.MaxX.HasValue ? NumberFormatting.Format(domain.MaxX.Value) : "t₀ + 10";
            DynamicsSummaryTitle.Text = dimensions switch
            {
                1 => $"{names[0]} over time",
                2 => $"Phase portrait: {names[0]} × {names[1]}",
                _ => $"3D state trajectory: {string.Join(" × ", names)}"
            };

            DynamicsSummaryText.Text = dimensions switch
            {
                1 => $"Horizontal is time t; vertical is {names[0]}. The curve shows how the state evolves from t={start} to {end}.",
                2 => $"Horizontal is {names[0]}; vertical is {names[1]}. Each point is both values at the same instant. This is not saying {names[1]} = f({names[0]}); the loop is the path the system follows through state space from t={start} to {end}. Play the timeline to watch the highlighted state move around it.",
                _ => $"The three axes are {names[0]}, {names[1]} and {names[2]}. The curve is the system's state moving through 3D state space from t={start} to {end}."
            };
        }

        private void UpdateDynamicsCurrentStateText()
        {
            if (_dynamicsInspectorSamples.Count == 0 || _dynamicsStateNames.Length == 0)
            {
                DynamicsCurrentStateText.Text = string.Empty;
                return;
            }

            double wanted = _timelineTime;
            DynamicalSystemSampler.Sample sample = _dynamicsInspectorSamples
                .OrderBy(s => Math.Abs(s.Time - wanted))
                .First();

            var parts = new List<string> { $"t={NumberFormatting.Format(sample.Time)}" };
            for (int i = 0; i < Math.Min(_dynamicsStateNames.Length, sample.Values.Length); i++)
            {
                double d = i < sample.Derivatives.Length ? sample.Derivatives[i] : double.NaN;
                parts.Add($"{_dynamicsStateNames[i]} {NumberFormatting.Format(sample.Values[i])} {FormatDerivativeTrend(d)}");
            }
            if (_dynamicsSelectedTime.HasValue && Math.Abs(_dynamicsSelectedTime.Value - sample.Time) > 1e-8)
                parts.Add($"selected t={NumberFormatting.Format(_dynamicsSelectedTime.Value)}");
            DynamicsCurrentStateText.Text = string.Join("   ·   ", parts);
        }

        private void RebuildDynamicsTable()
        {
            if (DynamicsDataGrid == null || _dynamicsInspectorSamples.Count == 0 || _dynamicsStateNames.Length == 0) return;

            int rowCount = 100;
            if (DynamicsTableRowsComboBox?.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Content?.ToString(), out int parsed)) rowCount = parsed;
            rowCount = Math.Clamp(rowCount, 10, 1000);
            bool showDerivatives = DynamicsTableDerivativesCheckBox?.IsChecked == true;

            var table = new DataTable();
            table.Locale = CultureInfo.InvariantCulture;
            _dynamicsTableTimes.Clear();
            table.Columns.Add("t", typeof(string));
            foreach (string name in _dynamicsStateNames) table.Columns.Add(name, typeof(string));
            if (showDerivatives)
                foreach (string name in _dynamicsStateNames) table.Columns.Add("d" + name + "/dt", typeof(string));

            int available = _dynamicsInspectorSamples.Count;
            for (int row = 0; row < Math.Min(rowCount, available); row++)
            {
                int index = rowCount <= 1 ? 0 : (int)Math.Round(row * (available - 1.0) / (Math.Min(rowCount, available) - 1.0));
                DynamicalSystemSampler.Sample sample = _dynamicsInspectorSamples[index];
                _dynamicsTableTimes.Add(sample.Time);
                DataRow data = table.NewRow();
                data["t"] = NumberFormatting.Format(sample.Time);
                for (int i = 0; i < _dynamicsStateNames.Length; i++)
                    data[_dynamicsStateNames[i]] = NumberFormatting.Format(sample.Values[i]);
                if (showDerivatives)
                    for (int i = 0; i < _dynamicsStateNames.Length; i++)
                        data["d" + _dynamicsStateNames[i] + "/dt"] = FormatDerivativeTrend(sample.Derivatives[i], includeLabel: false);
                table.Rows.Add(data);
            }

            DynamicsDataGrid.ItemsSource = table.DefaultView;
        }

        private static string FormatDerivativeTrend(double derivative, bool includeLabel = true)
        {
            if (!double.IsFinite(derivative)) return includeLabel ? "→ (—)" : "→ —";
            string arrow = Math.Abs(derivative) < 1e-9 ? "→" : derivative > 0 ? "↑" : "↓";
            string value = NumberFormatting.Format(derivative);
            if (derivative > 0) value = "+" + value;
            return includeLabel ? $"{arrow} ({value})" : $"{arrow} {value}";
        }

        private void RedrawDynamicsTimeSeries()
        {
            if (DynamicsTimeSeriesCanvas == null) return;
            Canvas canvas = DynamicsTimeSeriesCanvas;
            canvas.Children.Clear();
            if (_dynamicsInspectorSamples.Count < 2 || _dynamicsStateNames.Length == 0) return;

            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width < 80 || height < 70) return;

            const double left = 48;
            const double right = 12;
            const double top = 24;
            const double bottom = 28;
            double plotW = Math.Max(1, width - left - right);
            double plotH = Math.Max(1, height - top - bottom);

            double minT = _dynamicsInspectorSamples.Min(s => s.Time);
            double maxT = _dynamicsInspectorSamples.Max(s => s.Time);
            var finite = _dynamicsInspectorSamples.SelectMany(s => s.Values).Where(double.IsFinite).ToArray();
            if (finite.Length == 0 || maxT <= minT) return;
            double minV = finite.Min();
            double maxV = finite.Max();
            if (Math.Abs(maxV - minV) < 1e-12) { minV -= 1; maxV += 1; }
            double pad = (maxV - minV) * 0.08;
            minV -= pad;
            maxV += pad;

            Brush grid = ThemeBrush("GridLineBrush", Brushes.LightGray);
            Brush axis = ThemeBrush("AxisBrush", Brushes.Gray);
            Brush text = ThemeBrush("MutedTextBrush", Brushes.Gray);

            for (int i = 0; i <= 4; i++)
            {
                double x = left + plotW * i / 4.0;
                canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + plotH, Stroke = grid, StrokeThickness = 1 });
                double t = minT + (maxT - minT) * i / 4.0;
                var label = new TextBlock { Text = NumberFormatting.FormatAxis(t), Foreground = text, FontSize = 10 };
                Canvas.SetLeft(label, x - 8); Canvas.SetTop(label, top + plotH + 5); canvas.Children.Add(label);
            }
            for (int i = 0; i <= 4; i++)
            {
                double y = top + plotH * i / 4.0;
                canvas.Children.Add(new Line { X1 = left, X2 = left + plotW, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
                double v = maxV - (maxV - minV) * i / 4.0;
                var label = new TextBlock { Text = NumberFormatting.FormatAxis(v), Foreground = text, FontSize = 10 };
                Canvas.SetLeft(label, 3); Canvas.SetTop(label, y - 7); canvas.Children.Add(label);
            }
            canvas.Children.Add(new Line { X1 = left, X2 = left + plotW, Y1 = top + plotH, Y2 = top + plotH, Stroke = axis, StrokeThickness = 1.2 });
            canvas.Children.Add(new Line { X1 = left, X2 = left, Y1 = top, Y2 = top + plotH, Stroke = axis, StrokeThickness = 1.2 });

            for (int state = 0; state < _dynamicsStateNames.Length; state++)
            {
                Brush color = _palette[state % _palette.Length];
                var line = new Polyline { Stroke = color, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
                foreach (DynamicalSystemSampler.Sample sample in _dynamicsInspectorSamples)
                {
                    if (state >= sample.Values.Length || !double.IsFinite(sample.Values[state])) continue;
                    double x = left + (sample.Time - minT) / (maxT - minT) * plotW;
                    double y = top + (maxV - sample.Values[state]) / (maxV - minV) * plotH;
                    line.Points.Add(new Point(x, y));
                }
                canvas.Children.Add(line);

                var legend = new TextBlock { Text = _dynamicsStateNames[state], Foreground = color, FontWeight = FontWeights.SemiBold, FontSize = 10.5 };
                Canvas.SetLeft(legend, left + state * 90); Canvas.SetTop(legend, 3); canvas.Children.Add(legend);
            }

            // Keep the time-series guide in sync with the phase marker.
            if (_timelineTime >= minT && _timelineTime <= maxT)
            {
                double playX = left + (_timelineTime - minT) / (maxT - minT) * plotW;
                canvas.Children.Add(new Line
                {
                    X1 = playX, X2 = playX, Y1 = top, Y2 = top + plotH,
                    Stroke = ThemeBrush("AxisBrush", Brushes.Gray),
                    StrokeThickness = 1.2,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    Opacity = 0.9
                });

                DynamicalSystemSampler.Sample current = _dynamicsInspectorSamples
                    .OrderBy(sample => Math.Abs(sample.Time - _timelineTime))
                    .First();
                for (int state = 0; state < _dynamicsStateNames.Length && state < current.Values.Length; state++)
                {
                    if (!double.IsFinite(current.Values[state])) continue;
                    double y = top + (maxV - current.Values[state]) / (maxV - minV) * plotH;
                    Brush color = _palette[state % _palette.Length];
                    var dot = new Ellipse { Width = 7, Height = 7, Fill = color, Stroke = ThemeBrush("GraphBackgroundBrush", Brushes.Black), StrokeThickness = 1 };
                    Canvas.SetLeft(dot, playX - 3.5);
                    Canvas.SetTop(dot, y - 3.5);
                    canvas.Children.Add(dot);
                }
            }

            var tLabel = new TextBlock { Text = "t", Foreground = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(tLabel, left + plotW - 5); Canvas.SetTop(tLabel, top + plotH + 5); canvas.Children.Add(tLabel);
        }

        private void AppendDynamicsHover(Point pointer, double width, double height)
        {
            if (_dynamicsInspectorExpression == null || _dynamicsInspectorSamples.Count == 0) return;
            int dimensions = GetDynamicsDimensions(_dynamicsInspectorExpression.Kind);
            if (dimensions is < 1 or > 2) return;

            DynamicalSystemSampler.Sample? nearest = null;
            double nearestDistance = double.PositiveInfinity;
            foreach (DynamicalSystemSampler.Sample sample in _dynamicsInspectorSamples)
            {
                double x = dimensions == 1 ? sample.Time : sample.Values[0];
                double y = dimensions == 1 ? sample.Values[0] : sample.Values[1];
                if (!double.IsFinite(x) || !double.IsFinite(y)) continue;
                double px = TransformX(x, _viewport, width);
                double py = TransformY(y, _viewport, height);
                double dx = px - pointer.X, dy = py - pointer.Y;
                double distance = dx * dx + dy * dy;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = sample;
                }
            }

            if (nearest == null || nearestDistance > 28 * 28) return;
            DynamicalSystemSampler.Sample state = nearest;
            var parts = new List<string> { $"t={NumberFormatting.Format(state.Time)}" };
            for (int i = 0; i < _dynamicsStateNames.Length && i < state.Values.Length; i++)
            {
                double derivative = i < state.Derivatives.Length ? state.Derivatives[i] : double.NaN;
                string arrow = !double.IsFinite(derivative) || Math.Abs(derivative) < 1e-9 ? "→" : derivative > 0 ? "↑" : "↓";
                parts.Add($"{_dynamicsStateNames[i]}={NumberFormatting.Format(state.Values[i])} {arrow}");
            }
            HoverValuesList.Items.Insert(0, new TextBlock
            {
                Text = "state · " + string.Join("   ", parts),
                Foreground = _dynamicsInspectorExpression.Color,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                MaxWidth = 430,
                TextWrapping = TextWrapping.Wrap
            });
        }

        private void DrawDynamicsAnnotations(PlotViewport viewport, double width, double height)
        {
            GraphExpression? expression = GetPrimaryDynamicsExpression();
            if (expression == null || expression.Kind == GraphExpressionKind.DynamicalSystem3D) return;
            UpdateDynamicsInspector();
            if (_dynamicsInspectorSamples.Count < 2) return;

            Brush labelBrush = ThemeBrush("MutedTextBrush", Brushes.Gray);
            Brush color = expression.Color;
            int dimensions = GetDynamicsDimensions(expression.Kind);

            string xLabel = dimensions == 1 ? "t" : _dynamicsStateNames.ElementAtOrDefault(0) ?? "x";
            string yLabel = dimensions == 1 ? _dynamicsStateNames.ElementAtOrDefault(0) ?? "state" : _dynamicsStateNames.ElementAtOrDefault(1) ?? "y";
            var xText = new TextBlock { Text = xLabel + " →", Foreground = labelBrush, FontSize = 11, FontWeight = FontWeights.SemiBold };
            Canvas.SetRight(xText, 10); Canvas.SetBottom(xText, 6); PlotCanvas.Children.Add(xText);
            var yText = new TextBlock { Text = yLabel + " ↑", Foreground = labelBrush, FontSize = 11, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(yText, 8); Canvas.SetTop(yText, 6); PlotCanvas.Children.Add(yText);

            if (DynamicsShowDirectionCheckBox?.IsChecked == true && dimensions == 2)
            {
                int arrowCount = Math.Min(10, Math.Max(4, _dynamicsInspectorSamples.Count / 50));
                for (int a = 1; a <= arrowCount; a++)
                {
                    int index = (int)Math.Round(a * (_dynamicsInspectorSamples.Count - 2.0) / (arrowCount + 1.0));
                    DrawDirectionArrow(_dynamicsInspectorSamples[index], _dynamicsInspectorSamples[index + 1], color, viewport, width, height);
                }
            }

            if (DynamicsShowStartCheckBox?.IsChecked == true)
            {
                DynamicalSystemSampler.Sample start = FindInitialDynamicsSample(expression, _dynamicsInspectorSamples);
                DrawStateMarker(start, dimensions, color, viewport, width, height, 7, hollow: true);
            }

            DynamicalSystemSampler.Sample current = _dynamicsInspectorSamples.OrderBy(s => Math.Abs(s.Time - _timelineTime)).First();
            DrawStateMarker(current, dimensions, color, viewport, width, height, 8, hollow: false);

            if (_dynamicsSelectedTime.HasValue)
            {
                DynamicalSystemSampler.Sample selected = _dynamicsInspectorSamples.OrderBy(s => Math.Abs(s.Time - _dynamicsSelectedTime.Value)).First();
                DrawStateMarker(selected, dimensions, Brushes.Gold, viewport, width, height, 10, hollow: true);
            }
        }

        private DynamicalSystemSampler.Sample FindInitialDynamicsSample(GraphExpression expression, IReadOnlyList<DynamicalSystemSampler.Sample> samples)
        {
            int dimensions = GetDynamicsDimensions(expression.Kind);
            double initialT = samples[0].Time;
            try
            {
                if (expression.Components.Count == dimensions * 2 + 1)
                    initialT = expression.Components[dimensions * 2].Evaluate(GetParameterValues());
            }
            catch { }
            return samples.OrderBy(s => Math.Abs(s.Time - initialT)).First();
        }

        private void DrawStateMarker(DynamicalSystemSampler.Sample sample, int dimensions, Brush color, PlotViewport viewport, double width, double height, double size, bool hollow)
        {
            double x = dimensions == 1 ? sample.Time : sample.Values[0];
            double y = dimensions == 1 ? sample.Values[0] : sample.Values[1];
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            double px = TransformX(x, viewport, width);
            double py = TransformY(y, viewport, height);
            var ellipse = new Ellipse
            {
                Width = size,
                Height = size,
                Stroke = color,
                StrokeThickness = 2,
                Fill = hollow ? ThemeBrush("GraphBackgroundBrush", Brushes.White) : color
            };
            Canvas.SetLeft(ellipse, px - size / 2); Canvas.SetTop(ellipse, py - size / 2); PlotCanvas.Children.Add(ellipse);
        }

        private void DrawDirectionArrow(DynamicalSystemSampler.Sample a, DynamicalSystemSampler.Sample b, Brush color, PlotViewport viewport, double width, double height)
        {
            if (a.Values.Length < 2 || b.Values.Length < 2) return;
            double ax = TransformX(a.Values[0], viewport, width);
            double ay = TransformY(a.Values[1], viewport, height);
            double bx = TransformX(b.Values[0], viewport, width);
            double by = TransformY(b.Values[1], viewport, height);
            double dx = bx - ax, dy = by - ay;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length) || length < 0.5) return;
            dx /= length; dy /= length;
            double cx = ax, cy = ay;
            double half = 7;
            var shaft = new Line { X1 = cx - dx * half, Y1 = cy - dy * half, X2 = cx + dx * half, Y2 = cy + dy * half, Stroke = color, StrokeThickness = 1.5, Opacity = 0.85 };
            PlotCanvas.Children.Add(shaft);
            double tipX = cx + dx * half, tipY = cy + dy * half;
            double px = -dy, py = dx;
            var head = new Polygon
            {
                Fill = color,
                Opacity = 0.9,
                Points = new PointCollection
                {
                    new Point(tipX, tipY),
                    new Point(tipX - dx * 5 + px * 3, tipY - dy * 5 + py * 3),
                    new Point(tipX - dx * 5 - px * 3, tipY - dy * 5 - py * 3)
                }
            };
            PlotCanvas.Children.Add(head);
        }
    }
}
