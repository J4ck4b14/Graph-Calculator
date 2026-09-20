using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

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
        private readonly DispatcherTimer _animationTimer;
        private readonly Stopwatch _animationClock = Stopwatch.StartNew();
        private double _lastAnimationTime;
        private bool _updatingAnimatedParameters;
        private bool _animationRenderPending;
        private CancellationTokenSource? _renderCancellation;
        private CancellationTokenSource? _fitCancellation;
        private PlotViewport _viewport = new(-10, 10, -10, 10);
        private SurfaceViewport _surfaceViewport = new(-10, 10, -10, 10, -10, 10);
        private TextBox? _activeExpressionBox;
        private int _nextColorIndex;
        private bool _isDragging;
        private Point _dragStart;
        private PlotViewport _dragStartViewport;

        private bool _is3DMode;
        private bool _surfaceIsDragging;
        private Point _surfaceDragStart;
        private double _surfaceDragStartYaw;
        private double _surfaceDragStartPitch;
        private double _surfaceYaw = 38;
        private double _surfacePitch = 28;
        private double _surfaceCameraDistance = 18;
        private bool _loadingWorkspace;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _renderTimer.Tick += RenderTimer_Tick;

            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _animationTimer.Tick += AnimationTimer_Tick;
        }

        public ObservableCollection<GraphExpression> Expressions { get; } = new();
        public ObservableCollection<GraphParameter> Parameters { get; } = new();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Expressions.Count == 0)
            {
                AddExpression();
            }

            PresetComboBox.ItemsSource = PresetLibrary.Items;
            if (PresetLibrary.Items.Count > 0) PresetComboBox.SelectedIndex = 0;

            UpdatePlotRangeInputs();
            UpdateSurfaceRangeInputs();
            UpdateSurfaceCamera();
            UpdatePlotModeUi(refreshExpressions: false);
            InitializeAdvancedUi();
            InitializeProductPass();
            InitializeWorkspaceModes();
            InitializeIntegrationPass();
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
            _animationTimer.Stop();
            _economyTimer.Stop();
            _undoCaptureTimer?.Stop();
            _projectAutosaveTimer.Stop();

            foreach (GraphParameter parameter in Parameters)
            {
                parameter.PropertyChanged -= Parameter_PropertyChanged;
            }

            base.OnClosed(e);
        }

        private GraphExpression AddExpression(string expression = "")
        {
            var item = new GraphExpression(_palette[_nextColorIndex++ % _palette.Length])
            {
                Expression = expression,
                ShowYDomain = _is3DMode
            };

            item.PropertyChanged += Expression_PropertyChanged;
            Expressions.Add(item);
            RefreshExpression(item);
            SyncParameters();
            RefreshAllExpressionStatuses();
            return item;
        }

        private void AddExpressionButton_Click(object sender, RoutedEventArgs e)
        {
            var item = AddExpression();
            MarkWorkspaceEdit();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusExpression(item)));
        }

        private void RemoveExpressionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: GraphExpression item }) return;

            item.PropertyChanged -= Expression_PropertyChanged;
            Expressions.Remove(item);
            MarkWorkspaceEdit();

            if (_activeExpressionBox?.DataContext == item)
            {
                _activeExpressionBox = null;
            }

            if (Expressions.Count == 0)
            {
                AddExpression();
            }
            else
            {
                SyncParameters();
            }

            ScheduleRender(20);
        }

        private void Expression_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not GraphExpression item) return;

            if (e.PropertyName == nameof(GraphExpression.Expression))
            {
                RefreshExpression(item);
                SyncParameters();
                MarkWorkspaceEdit();
                RefreshAllExpressionStatuses();
                ScheduleRender(180);
            }
            else if (e.PropertyName == nameof(GraphExpression.IsVisible))
            {
                MarkWorkspaceEdit();
                ScheduleRender(30);
            }
            else if (e.PropertyName is nameof(GraphExpression.DomainMinX)
                or nameof(GraphExpression.DomainMaxX)
                or nameof(GraphExpression.DomainMinY)
                or nameof(GraphExpression.DomainMaxY))
            {
                SyncParameters();
                MarkWorkspaceEdit();
                RefreshAllExpressionStatuses();
                ScheduleRender(120);
            }
        }

        private void RefreshExpression(GraphExpression item)
        {
            item.Compiled = null;
            item.Components = [];
            item.Kind = GraphExpressionKind.Scalar;

            if (string.IsNullOrWhiteSpace(item.Expression))
            {
                item.StatusText = string.Empty;
                return;
            }

            try
            {
                if (PlotExpressionParser.TryParseParametric(
                        item.Expression,
                        out GraphExpressionKind kind,
                        out IReadOnlyList<string> components,
                        out string? specialError))
                {
                    if (specialError != null) throw new FormatException(specialError);

                    item.Kind = kind;
                    item.Compiled = null;
                    item.Components = components.Select(c => CalculatorEngine.Compile(ExpandSharedAssets(c))).ToArray();
                }
                else
                {
                    item.Kind = GraphExpressionKind.Scalar;
                    item.Components = [];
                    item.Compiled = CalculatorEngine.Compile(ExpandSharedAssets(item.Expression));
                }

                UpdateExpressionStatus(item);
            }
            catch (Exception ex)
            {
                item.Compiled = null;
                item.Components = [];
                item.StatusText = "Error: " + ex.Message;
            }
        }

        private void UpdateExpressionStatus(GraphExpression item)
        {
            if (!item.CompiledParts.Any()) return;

            if ((item.Kind is GraphExpressionKind.Parametric2D or GraphExpressionKind.VectorField2D or GraphExpressionKind.Implicit2D or GraphExpressionKind.TextureField2D) && _is3DMode)
            {
                item.StatusText = "2D plot — switch to 2D";
                return;
            }
            if ((item.Kind is GraphExpressionKind.Parametric3D or GraphExpressionKind.ParametricSurface3D or GraphExpressionKind.Implicit3D) && !_is3DMode)
            {
                item.StatusText = "3D plot — switch to 3D";
                return;
            }
            if (item.Kind == GraphExpressionKind.VectorField2D && _is3DMode)
            {
                item.StatusText = "Vector field — switch to 2D";
                return;
            }
            if (item.Kind == GraphExpressionKind.Scalar && !_is3DMode && item.Compiled?.DependsOnY == true)
            {
                item.StatusText = "Uses y — switch to 3D";
                return;
            }

            if ((item.Kind is GraphExpressionKind.Parametric2D or GraphExpressionKind.Parametric3D)
                && item.Components.Any(component => component.DependsOnX || component.DependsOnY))
            {
                item.StatusText = "Parametric curves use t as their axis variable";
                return;
            }
            if (item.Kind == GraphExpressionKind.ParametricSurface3D
                && item.Components.Any(component => component.DependsOnX || component.DependsOnY))
            {
                item.StatusText = "Parametric surfaces use u and v; x/y are outputs";
                return;
            }

            Dictionary<string, double> values = GetParameterValues();
            if (!TryReadExpressionDomain(item, values, out ExpressionDomain domain, out string? rangeError))
            {
                item.StatusText = "Range: " + rangeError;
                return;
            }

            string[] customVariables = item.Variables
                .Where(name => !IsReservedVariable(name, item.Kind))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string parameters = customVariables.Length == 0
                ? string.Empty
                : "   " + string.Join("   ", customVariables.Select(name =>
                    $"{name} = {NumberFormatting.Format(values.TryGetValue(name, out double value) ? value : 0)}"));

            switch (item.Kind)
            {
                case GraphExpressionKind.Parametric2D:
                case GraphExpressionKind.Parametric3D:
                {
                    string range = $"t: {FormatOptionalBound(domain.MinX, "0")} to {FormatOptionalBound(domain.MaxX, "2π")}";
                    item.StatusText = (item.Kind == GraphExpressionKind.Parametric3D ? "3D curve   " : "Parametric curve   ") + range + parameters;
                    return;
                }
                case GraphExpressionKind.ParametricSurface3D:
                {
                    string u = $"u: {FormatOptionalBound(domain.MinX, "0")} to {FormatOptionalBound(domain.MaxX, "2π")}";
                    string v = $"v: {FormatOptionalBound(domain.MinY, "0")} to {FormatOptionalBound(domain.MaxY, "2π")}";
                    item.StatusText = $"Parametric surface   {u}   {v}" + parameters;
                    return;
                }
                case GraphExpressionKind.VectorField2D:
                    item.StatusText = "Vector field   vx, vy" + parameters;
                    return;
                case GraphExpressionKind.Implicit2D:
                    item.StatusText = "Implicit contour   f(x,y) = 0" + parameters;
                    return;
                case GraphExpressionKind.Implicit3D:
                    item.StatusText = "Implicit isosurface   f(x,y,z) = 0" + parameters;
                    return;
                case GraphExpressionKind.TextureField2D:
                    item.StatusText = "2D scalar field / texture" + parameters;
                    return;
            }

            CalculatorEngine.CompiledExpression compiled = item.Compiled!;
            if (!compiled.DependsOnX && !compiled.DependsOnY)
            {
                try
                {
                    item.StatusText = "= " + NumberFormatting.Format(compiled.Evaluate(values));
                }
                catch (Exception ex)
                {
                    item.StatusText = "Error: " + ex.Message;
                }
                return;
            }

            item.StatusText = customVariables.Length > 0
                ? string.Join("   ", customVariables.Select(name =>
                    $"{name} = {NumberFormatting.Format(values.TryGetValue(name, out double value) ? value : 0)}"))
                : string.Empty;
        }

        private static string FormatOptionalBound(double? value, string fallback)
        {
            return value.HasValue ? NumberFormatting.Format(value.Value) : fallback;
        }

        private void RefreshAllExpressionStatuses()
        {
            foreach (GraphExpression item in Expressions)
            {
                if (item.CompiledParts.Any())
                {
                    UpdateExpressionStatus(item);
                }
            }
        }

        private void SyncParameters()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (GraphExpression item in Expressions)
            {
                foreach (string name in item.Variables)
                {
                    if (!IsReservedVariable(name, item.Kind)) names.Add(name);
                }

                foreach (string rangeText in new[] { item.DomainMinX, item.DomainMaxX, item.DomainMinY, item.DomainMaxY })
                {
                    if (string.IsNullOrWhiteSpace(rangeText)) continue;

                    try
                    {
                        CalculatorEngine.CompiledExpression rangeExpression = CalculatorEngine.Compile(ExpandSharedAssets(rangeText));
                        foreach (string name in rangeExpression.Variables)
                        {
                            if (!IsReservedVariable(name, item.Kind)) names.Add(name);
                        }
                    }
                    catch
                    {
                        // Half-typed bounds should not make neigbouring controls flicker in and out.
                    }
                }
            }

            for (int i = Parameters.Count - 1; i >= 0; i--)
            {
                if (names.Contains(Parameters[i].Name)) continue;
                Parameters[i].PropertyChanged -= Parameter_PropertyChanged;
                Parameters.RemoveAt(i);
            }

            var existing = new HashSet<string>(Parameters.Select(parameter => parameter.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string name in names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (existing.Contains(name)) continue;
                var parameter = new GraphParameter(name);
                parameter.PropertyChanged += Parameter_PropertyChanged;
                Parameters.Add(parameter);
            }

            ParametersExpander.Visibility = Parameters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateAnimationTimerState();
        }

        private void Parameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_loadingWorkspace) return;
            if (!_updatingAnimatedParameters && !_applyingParameterState) MarkWorkspaceEdit();

            if (e.PropertyName == nameof(GraphParameter.Group))
            {
                CollectionViewSource.GetDefaultView(Parameters).Refresh();
                return;
            }

            if (e.PropertyName is nameof(GraphParameter.IsAnimating) or nameof(GraphParameter.AnimationSpeed) or nameof(GraphParameter.IsLocked))
            {
                if (!_updatingAnimatedParameters)
                {
                    UpdateAnimationTimerState();
                }
                return;
            }

            if (e.PropertyName != nameof(GraphParameter.Value) || _updatingAnimatedParameters) return;

            RefreshAllExpressionStatuses();
            ScheduleRender(20);
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            double now = _animationClock.Elapsed.TotalSeconds;
            double deltaSeconds = Math.Clamp(now - _lastAnimationTime, 0, 0.12);
            _lastAnimationTime = now;

            bool changed = AdvanceTimeline(deltaSeconds);
            _updatingAnimatedParameters = true;
            try
            {
                foreach (GraphParameter parameter in Parameters)
                {
                    changed |= parameter.AdvanceAnimation(deltaSeconds);
                }
            }
            finally
            {
                _updatingAnimatedParameters = false;
            }

            if (changed)
            {
                RefreshAllExpressionStatuses();

                if (_renderCancellation == null && !_renderTimer.IsEnabled)
                {
                    ScheduleRender(1);
                }
                else
                {
                    _animationRenderPending = true;
                }
            }

            UpdateAnimationTimerState();
        }

        private void UpdateAnimationTimerState()
        {
            bool shouldRun = _timelinePlaying
                || Parameters.Any(parameter => parameter.IsAnimating && parameter.AnimationSpeed > 0);

            if (shouldRun)
            {
                if (!_animationTimer.IsEnabled)
                {
                    _lastAnimationTime = _animationClock.Elapsed.TotalSeconds;
                    _animationTimer.Start();
                }
            }
            else
            {
                _animationTimer.Stop();
            }
        }

        private void ResetParameterAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: GraphParameter parameter }) return;

            parameter.ResetAnimation();
            RefreshAllExpressionStatuses();
            ScheduleRender(0);
        }

        private void FlushPendingAnimationRender()
        {
            if (!_animationRenderPending || _renderCancellation != null) return;
            _animationRenderPending = false;
            ScheduleRender(1);
        }

        private Dictionary<string, double> GetParameterValues()
        {
            // t/time belong to the transport. Parametric samplers overwrite t locally when it is their axis.
            Dictionary<string, double> values = Parameters.ToDictionary(
                parameter => parameter.Name, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);
            values["t"] = _timelineTime;
            values["time"] = _timelineTime;
            return values;
        }

        private static bool IsAxisVariable(string name)
        {
            return name.Equals("x", StringComparison.OrdinalIgnoreCase)
                || name.Equals("y", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReservedVariable(string name, GraphExpressionKind kind)
        {
            if (name.Equals("time", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals("t", StringComparison.OrdinalIgnoreCase)) return true;
            if (kind == GraphExpressionKind.ParametricSurface3D
                && (name.Equals("u", StringComparison.OrdinalIgnoreCase) || name.Equals("v", StringComparison.OrdinalIgnoreCase))) return true;
            if (kind == GraphExpressionKind.Implicit3D && name.Equals("z", StringComparison.OrdinalIgnoreCase)) return true;
            return IsAxisVariable(name);
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

            if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                OpenFunctionSuggestions(box);
                e.Handled = true;
                return;
            }

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
            if (!IsLoaded || _loadingWorkspace) return;

            _renderTimer.Stop();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMilliseconds));
            _renderTimer.Start();
        }

        private async Task RenderGraphAsync()
        {
            _animationRenderPending = false;

            if (_workspaceMode == WorkspaceMode.CurveDesigner)
            {
                RenderCurveDesigner();
                return;
            }
            if (_workspaceMode == WorkspaceMode.EconomyDesigner) return;

            if (_is3DMode)
            {
                await RenderSurfaceGraphAsync();
                return;
            }

            double width = GraphSurface.ActualWidth;
            double height = GraphSurface.ActualHeight;
            if (width < 20 || height < 20) return;

            _renderCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _renderCancellation = cancellation;
            CancellationToken token = cancellation.Token;

            Dictionary<string, double> parameterValues = GetParameterValues();
            List<SeriesRequest> active = GetActiveSeriesRequests(threeDimensional: false, parameterValues);
            List<SeriesRequest> lineSeries = active
                .Where(request => request.Kind is GraphExpressionKind.Scalar or GraphExpressionKind.Parametric2D)
                .ToList();
            List<SeriesRequest> fields = active
                .Where(request => request.Kind == GraphExpressionKind.VectorField2D)
                .ToList();
            List<SeriesRequest> implicitContours = active
                .Where(request => request.Kind == GraphExpressionKind.Implicit2D)
                .ToList();

            PlotViewport viewport = _viewport;
            int pixelWidth = Math.Max(1, (int)Math.Round(width));
            int pixelHeight = Math.Max(1, (int)Math.Round(height));

            try
            {
                List<List<GraphPoint>> samples = await Task.Run(() =>
                {
                    var result = new List<List<GraphPoint>>(lineSeries.Count);
                    foreach (SeriesRequest request in lineSeries)
                    {
                        token.ThrowIfCancellationRequested();
                        if (request.Kind == GraphExpressionKind.Parametric2D)
                        {
                            result.Add(ParametricCurveSampler.Sample2D(
                                request.Components, parameterValues, request.Domain.MinX, request.Domain.MaxX,
                                GetLineSampleCount(pixelWidth), token));
                        }
                        else
                        {
                            result.Add(GraphSampler.Sample(
                                request.Compiled!, viewport, pixelWidth, pixelHeight, parameterValues,
                                request.Domain.MinX, request.Domain.MaxX, token));
                        }
                    }
                    return result;
                }, token);

                List<List<GraphSegment>> contourSamples = await Task.Run(() =>
                {
                    var result = new List<List<GraphSegment>>(implicitContours.Count);
                    foreach (SeriesRequest request in implicitContours)
                    {
                        token.ThrowIfCancellationRequested();
                        result.Add(ImplicitSampler.Sample2D(
                            request.Components[0], viewport, parameterValues,
                            request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                            GetImplicit2DResolution(), token));
                    }
                    return result;
                }, token);

                token.ThrowIfCancellationRequested();
                UpdateFieldPreview(parameterValues);
                PlotCanvas.Children.Clear();
                DrawGridAndAxes(viewport, width, height);

                for (int i = 0; i < lineSeries.Count; i++)
                {
                    DrawSeries(samples[i], lineSeries[i].Expression.Color, viewport, width, height);
                }

                foreach (SeriesRequest field in fields)
                {
                    DrawVectorField(field, parameterValues, width, height);
                }

                for (int i = 0; i < implicitContours.Count; i++)
                {
                    DrawImplicitContours(contourSamples[i], implicitContours[i].Expression.Color, width, height);
                }

                DrawDerivativeOverlays(parameterValues, width, height);
                DrawComparisonOverlay(parameterValues, width, height);

                int parametricCount = lineSeries.Count(request => request.Kind == GraphExpressionKind.Parametric2D);
                int scalarCount = lineSeries.Count - parametricCount;
                int textureCount = active.Count(request => request.Kind == GraphExpressionKind.TextureField2D);
                GraphStatusText.Text = active.Count == 0
                    ? "No visible plots"
                    : $"{scalarCount} functions · {parametricCount} curves · {fields.Count} vector fields · {implicitContours.Count} contours · {textureCount} fields";

                UpdateViewRangeText();
            }
            catch (OperationCanceledException)
            {
                // A new edit wins; there is no point finishing a stale sampling pass.
            }
            finally
            {
                if (ReferenceEquals(_renderCancellation, cancellation)) _renderCancellation = null;
                cancellation.Dispose();
                FlushPendingAnimationRender();
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
            Dictionary<string, double> parameterValues = GetParameterValues();
            List<SeriesRequest> active = GetActiveSeriesRequests(_is3DMode, parameterValues);
            if (active.Count == 0)
            {
                GraphStatusText.Text = "Nothing to fit";
                return;
            }
            if (TryFitDomainOnlyPlots(active)) return;

            _fitCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _fitCancellation = cancellation;
            GraphStatusText.Text = "Fitting…";

            try
            {
                if (_is3DMode)
                {
                    List<SeriesRequest> scalarSurfaces = active.Where(r => r.Kind == GraphExpressionKind.Scalar).ToList();
                    List<SeriesRequest> curves = active.Where(r => r.Kind == GraphExpressionKind.Parametric3D).ToList();
                    List<SeriesRequest> parametricSurfaces = active.Where(r => r.Kind == GraphExpressionKind.ParametricSurface3D).ToList();

                    (double minX, double maxX, double minY, double maxY, double minZ, double maxZ)? geometryBounds =
                        await Task.Run(() =>
                        {
                            (double minX, double maxX, double minY, double maxY, double minZ, double maxZ)? combined = null;
                            foreach (SeriesRequest request in curves)
                            {
                                cancellation.Token.ThrowIfCancellationRequested();
                                List<GraphPoint3D> points = ParametricCurveSampler.Sample3D(
                                    request.Components, parameterValues, request.Domain.MinX, request.Domain.MaxX, 900, cancellation.Token);
                                var bounds = ParametricCurveSampler.FindBounds3D(points);
                                if (bounds.HasValue) combined = Union3D(combined, bounds.Value);
                            }

                            foreach (SeriesRequest request in parametricSurfaces)
                            {
                                cancellation.Token.ThrowIfCancellationRequested();
                                ParametricSurfaceSample sample = ParametricSurfaceSampler.Sample(
                                    request.Components, parameterValues,
                                    request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                                    56, cancellation.Token);
                                var bounds = ParametricSurfaceSampler.FindBounds(sample);
                                if (bounds.HasValue) combined = Union3D(combined, bounds.Value);
                            }
                            return combined;
                        }, cancellation.Token);

                    (double minZ, double maxZ)? scalarBounds = null;
                    if (scalarSurfaces.Count > 0)
                    {
                        var requests = scalarSurfaces.Select(request => new SurfaceSeriesSampleRequest(
                            request.Compiled!, request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY)).ToList();
                        scalarBounds = await Task.Run(
                            () => SurfaceSampler.FindZBounds(requests, _surfaceViewport, parameterValues, cancellation.Token),
                            cancellation.Token);
                    }

                    if (!geometryBounds.HasValue && !scalarBounds.HasValue)
                    {
                        GraphStatusText.Text = "No finite values in range";
                        return;
                    }

                    double minX = scalarSurfaces.Count > 0 ? _surfaceViewport.MinX : geometryBounds!.Value.minX;
                    double maxX = scalarSurfaces.Count > 0 ? _surfaceViewport.MaxX : geometryBounds!.Value.maxX;
                    double minY = scalarSurfaces.Count > 0 ? _surfaceViewport.MinY : geometryBounds!.Value.minY;
                    double maxY = scalarSurfaces.Count > 0 ? _surfaceViewport.MaxY : geometryBounds!.Value.maxY;

                    if (geometryBounds.HasValue && scalarSurfaces.Count > 0)
                    {
                        minX = Math.Min(minX, geometryBounds.Value.minX);
                        maxX = Math.Max(maxX, geometryBounds.Value.maxX);
                        minY = Math.Min(minY, geometryBounds.Value.minY);
                        maxY = Math.Max(maxY, geometryBounds.Value.maxY);
                    }

                    double minZ = scalarBounds?.minZ ?? geometryBounds!.Value.minZ;
                    double maxZ = scalarBounds?.maxZ ?? geometryBounds!.Value.maxZ;
                    if (geometryBounds.HasValue)
                    {
                        minZ = Math.Min(minZ, geometryBounds.Value.minZ);
                        maxZ = Math.Max(maxZ, geometryBounds.Value.maxZ);
                    }

                    if (geometryBounds.HasValue)
                    {
                        (minX, maxX) = PadPair(minX, maxX);
                        (minY, maxY) = PadPair(minY, maxY);
                    }
                    (minZ, maxZ) = PadPair(minZ, maxZ);
                    _surfaceViewport = new SurfaceViewport(minX, maxX, minY, maxY, minZ, maxZ);
                    UpdateSurfaceRangeInputs();
                }
                else
                {
                    List<SeriesRequest> scalar = active.Where(r => r.Kind == GraphExpressionKind.Scalar).ToList();
                    List<SeriesRequest> curves = active.Where(r => r.Kind == GraphExpressionKind.Parametric2D).ToList();
                    List<SeriesRequest> fields = active.Where(r => r.Kind == GraphExpressionKind.VectorField2D).ToList();

                    if (scalar.Count == 0 && curves.Count == 0 && fields.Count > 0)
                    {
                        GraphStatusText.Text = "Vector fields use the current view range";
                        return;
                    }

                    double minX = _viewport.MinX;
                    double maxX = _viewport.MaxX;
                    double? minY = null;
                    double? maxY = null;

                    if (scalar.Count > 0)
                    {
                        var requests = scalar.Select(request => new GraphSeriesSampleRequest(
                            request.Compiled!, request.Domain.MinX, request.Domain.MaxX)).ToList();
                        var bounds = await Task.Run(
                            () => GraphSampler.FindYBounds(requests, _viewport.MinX, _viewport.MaxX, parameterValues, cancellation.Token),
                            cancellation.Token);
                        if (bounds.HasValue) { minY = bounds.Value.minY; maxY = bounds.Value.maxY; }
                    }

                    if (curves.Count > 0)
                    {
                        var curveBounds = await Task.Run(() =>
                        {
                            (double minX, double maxX, double minY, double maxY)? combined = null;
                            foreach (SeriesRequest request in curves)
                            {
                                List<GraphPoint> points = ParametricCurveSampler.Sample2D(
                                    request.Components, parameterValues, request.Domain.MinX, request.Domain.MaxX, 1200, cancellation.Token);
                                var bounds = ParametricCurveSampler.FindBounds2D(points);
                                if (bounds.HasValue) combined = Union2D(combined, bounds.Value);
                            }
                            return combined;
                        }, cancellation.Token);

                        if (curveBounds.HasValue)
                        {
                            if (scalar.Count == 0) { minX = curveBounds.Value.minX; maxX = curveBounds.Value.maxX; }
                            else { minX = Math.Min(minX, curveBounds.Value.minX); maxX = Math.Max(maxX, curveBounds.Value.maxX); }
                            minY = minY.HasValue ? Math.Min(minY.Value, curveBounds.Value.minY) : curveBounds.Value.minY;
                            maxY = maxY.HasValue ? Math.Max(maxY.Value, curveBounds.Value.maxY) : curveBounds.Value.maxY;
                        }
                    }

                    if (!minY.HasValue || !maxY.HasValue)
                    {
                        GraphStatusText.Text = "No finite values in view";
                        return;
                    }

                    if (curves.Count > 0) (minX, maxX) = PadPair(minX, maxX);
                    (double paddedMinY, double paddedMaxY) = PadPair(minY.Value, maxY.Value);
                    _viewport = new PlotViewport(minX, maxX, paddedMinY, paddedMaxY);
                    UpdatePlotRangeInputs();
                }

                UpdateViewRangeText();
                ScheduleRender(0);
                GraphStatusText.Text = "Fit to visible data";
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_fitCancellation, cancellation)) _fitCancellation = null;
                cancellation.Dispose();
            }
        }

        private static (double min, double max) PadPair(double min, double max)
        {
            if (!double.IsFinite(min) || !double.IsFinite(max)) return (-1, 1);
            if (Math.Abs(max - min) < 1e-10)
            {
                double pad = Math.Max(1.0, Math.Abs(min) * 0.15);
                return (min - pad, max + pad);
            }

            double padding = (max - min) * 0.08;
            return (min - padding, max + padding);
        }

        private static (double minX, double maxX, double minY, double maxY)? Union2D(
            (double minX, double maxX, double minY, double maxY)? a,
            (double minX, double maxX, double minY, double maxY) b)
        {
            if (!a.HasValue) return b;
            return (Math.Min(a.Value.minX, b.minX), Math.Max(a.Value.maxX, b.maxX),
                Math.Min(a.Value.minY, b.minY), Math.Max(a.Value.maxY, b.maxY));
        }

        private static (double minX, double maxX, double minY, double maxY, double minZ, double maxZ)? Union3D(
            (double minX, double maxX, double minY, double maxY, double minZ, double maxZ)? a,
            (double minX, double maxX, double minY, double maxY, double minZ, double maxZ) b)
        {
            if (!a.HasValue) return b;
            return (Math.Min(a.Value.minX, b.minX), Math.Max(a.Value.maxX, b.maxX),
                Math.Min(a.Value.minY, b.minY), Math.Max(a.Value.maxY, b.maxY),
                Math.Min(a.Value.minZ, b.minZ), Math.Max(a.Value.maxZ, b.maxZ));
        }

        private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_is3DMode)
            {
                _surfaceViewport = new SurfaceViewport(-10, 10, -10, 10, -10, 10);
                _surfaceYaw = 38;
                _surfacePitch = 28;
                _surfaceCameraDistance = 18;
                UpdateSurfaceRangeInputs();
                UpdateSurfaceCamera();
            }
            else
            {
                _viewport = new PlotViewport(-10, 10, -10, 10);
                UpdatePlotRangeInputs();
            }

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
            UpdatePlotRangeInputs();
            UpdateViewRangeText();
            ScheduleRender(25);
            UpdateHover(point);
            e.Handled = true;
        }

        private void PlotCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point curvePoint = e.GetPosition(PlotCanvas);
            if (CurveDesignerMouseDown(curvePoint, e)) return;

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
            if (CurveDesignerMouseUp(e)) return;
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

            if (CurveDesignerMouseMove(point, e)) return;

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
                UpdatePlotRangeInputs();
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

            if (_workspaceMode == WorkspaceMode.CurveDesigner)
            {
                double curveY = CurveDesignerMath.Evaluate(CurveKeys.ToList(), x, _curveBeforeMode, _curveAfterMode);
                if (double.IsFinite(curveY))
                {
                    HoverValuesList.Items.Add(new TextBlock
                    {
                        Text = $"curve(x) = {NumberFormatting.Format(curveY)}",
                        Foreground = Brushes.RoyalBlue,
                        FontSize = 12
                    });
                }
                HoverPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(HoverPanel, Math.Max(4, Math.Min(width - HoverPanel.DesiredSize.Width - 4, point.X + 14)));
                Canvas.SetTop(HoverPanel, Math.Max(4, Math.Min(height - HoverPanel.DesiredSize.Height - 4, point.Y + 14)));
                HoverPanel.Visibility = Visibility.Visible;
                return;
            }

            Dictionary<string, double> parameterValues = GetParameterValues();

            foreach (GraphExpression item in Expressions.Where(item => item.IsVisible))
            {
                CalculatorEngine.CompiledExpression? compiled = item.Compiled;
                if (compiled == null || compiled.DependsOnY) continue;
                if (!TryReadExpressionDomain(item, parameterValues, out ExpressionDomain domain, out _, includeYOverride: false)) continue;
                if ((domain.MinX.HasValue && x < domain.MinX.Value) || (domain.MaxX.HasValue && x > domain.MaxX.Value)) continue;

                double value;
                double derivative = double.NaN;
                try
                {
                    value = compiled.Evaluate(x, parameterValues);
                    double h = Math.Max(_viewport.Width * 1e-5, 1e-8);
                    double left = compiled.Evaluate(x - h, parameterValues);
                    double right = compiled.Evaluate(x + h, parameterValues);
                    derivative = (right - left) / (2 * h);
                }
                catch
                {
                    value = double.NaN;
                }

                string derivativeText = double.IsFinite(derivative) ? $"   dy/dx = {NumberFormatting.Format(derivative)}" : string.Empty;
                HoverValuesList.Items.Add(new TextBlock
                {
                    Text = $"{item.Expression} = {NumberFormatting.Format(value)}{derivativeText}",
                    Foreground = item.Color,
                    FontSize = 12,
                    MaxWidth = 380,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            foreach (GraphExpression item in Expressions.Where(item => item.IsVisible && item.Kind == GraphExpressionKind.VectorField2D && item.Components.Count == 2))
            {
                if (!TryReadExpressionDomain(item, parameterValues, out ExpressionDomain domain, out _, includeYOverride: true)) continue;
                if ((domain.MinX.HasValue && x < domain.MinX.Value) || (domain.MaxX.HasValue && x > domain.MaxX.Value)
                    || (domain.MinY.HasValue && y < domain.MinY.Value) || (domain.MaxY.HasValue && y > domain.MaxY.Value)) continue;

                var values = new Dictionary<string, double>(parameterValues, StringComparer.OrdinalIgnoreCase) { ["x"] = x, ["y"] = y };
                try
                {
                    double vx = item.Components[0].Evaluate(values);
                    double vy = item.Components[1].Evaluate(values);
                    double magnitude = Math.Sqrt(vx * vx + vy * vy);
                    HoverValuesList.Items.Add(new TextBlock
                    {
                        Text = $"{item.Expression} → ({NumberFormatting.Format(vx)}, {NumberFormatting.Format(vy)})   |v| = {NumberFormatting.Format(magnitude)}",
                        Foreground = item.Color,
                        FontSize = 12,
                        MaxWidth = 380,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                catch { }
            }

            HoverPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double panelWidth = HoverPanel.DesiredSize.Width;
            double panelHeight = HoverPanel.DesiredSize.Height;
            double leftPanel = point.X + 14;
            double topPanel = point.Y + 14;
            if (leftPanel + panelWidth > width) leftPanel = point.X - panelWidth - 14;
            if (topPanel + panelHeight > height) topPanel = point.Y - panelHeight - 14;

            Canvas.SetLeft(HoverPanel, Math.Max(4, leftPanel));
            Canvas.SetTop(HoverPanel, Math.Max(4, topPanel));
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
            if (_is3DMode)
            {
                ViewRangeText.Text =
                    $"x: {NumberFormatting.FormatAxis(_surfaceViewport.MinX)} to {NumberFormatting.FormatAxis(_surfaceViewport.MaxX)}   " +
                    $"y: {NumberFormatting.FormatAxis(_surfaceViewport.MinY)} to {NumberFormatting.FormatAxis(_surfaceViewport.MaxY)}   " +
                    $"z: {NumberFormatting.FormatAxis(_surfaceViewport.MinZ)} to {NumberFormatting.FormatAxis(_surfaceViewport.MaxZ)}";
                return;
            }

            ViewRangeText.Text =
                $"x: {NumberFormatting.FormatAxis(_viewport.MinX)} to {NumberFormatting.FormatAxis(_viewport.MaxX)}   " +
                $"y: {NumberFormatting.FormatAxis(_viewport.MinY)} to {NumberFormatting.FormatAxis(_viewport.MaxY)}";
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PresetDescriptionText.Text = PresetComboBox.SelectedItem is GraphPreset preset
                ? preset.Description
                : string.Empty;
        }

        private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetComboBox.SelectedItem is not GraphPreset preset) return;

            _loadingWorkspace = true;
            try
            {
                // Presets are ingredients, not documents. Keep whatever the user already built.
                if (Expressions.Count == 1 && string.IsNullOrWhiteSpace(Expressions[0].Expression))
                {
                    Expressions[0].PropertyChanged -= Expression_PropertyChanged;
                    Expressions.Clear();
                    _nextColorIndex = 0;
                }

                _is3DMode = preset.ThreeDimensional;
                PlotModeComboBox.SelectedIndex = _is3DMode ? 1 : 0;
                if (preset.PlotView.HasValue) _viewport = preset.PlotView.Value;
                if (preset.SurfaceView.HasValue) _surfaceViewport = preset.SurfaceView.Value;

                foreach (PresetExpression source in preset.Expressions)
                {
                    GraphExpression item = AddExpression(source.Expression);
                    item.DomainMinX = source.DomainMinX;
                    item.DomainMaxX = source.DomainMaxX;
                    item.DomainMinY = source.DomainMinY;
                    item.DomainMaxY = source.DomainMaxY;
                }

                if (Expressions.Count == 0) AddExpression();
                SyncParameters();
            }
            finally
            {
                _loadingWorkspace = false;
            }

            SetWorkspaceMode(WorkspaceMode.FunctionLab, updateCombo: true);
            RefreshParameterGroups();
            UpdatePlotRangeInputs();
            UpdateSurfaceRangeInputs();
            UpdatePlotModeUi(refreshExpressions: true);
            UpdateSurfaceCamera();
            UpdateViewRangeText();
            UpdateParameterStateBlendText();
            GraphStatusText.Text = $"Added preset: {preset.Name}";
            MarkWorkspaceEdit();
            ScheduleRender(0);
        }


        private WorkspaceFile CaptureWorkspaceForSave()
        {
            SaveActiveCurveChannel();
            return new WorkspaceFile
                {
                    WorkspaceMode = _workspaceMode.ToString(),
                    Is3DMode = _is3DMode,
                    PlotViewport = _viewport,
                    SurfaceViewport = _surfaceViewport,
                    SurfaceYaw = _surfaceYaw,
                    SurfacePitch = _surfacePitch,
                    SurfaceCameraDistance = _surfaceCameraDistance,
                    SurfaceDisplayMode = _surfaceDisplayMode.ToString(),
                    TimelineStart = _timelineStart,
                    TimelineEnd = _timelineEnd,
                    TimelineTime = _timelineTime,
                    TimelineSpeed = _timelineSpeed,
                    TimelineLoop = _timelineLoop,
                    ShowDerivative = ShowDerivativeCheckBox.IsChecked == true,
                    ComparisonEnabled = ComparisonEnabledCheckBox.IsChecked == true,
                    ComparisonMode = (ComparisonModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A - B",
                    ComparisonAIndex = Math.Max(0, ComparisonAComboBox.SelectedIndex),
                    ComparisonBIndex = Math.Max(0, ComparisonBComboBox.SelectedIndex),
                    CrossSectionEnabled = CrossSectionEnabledCheckBox.IsChecked == true,
                    CrossSectionAxis = (CrossSectionAxisComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Y",
                    CrossSectionValue = CrossSectionValueTextBox.Text,
                    CrossSectionExpressionIndex = Math.Max(0, CrossSectionExpressionComboBox.SelectedIndex),
                    RenderQuality = _renderQuality.ToString(),
                    FieldPreviewEnabled = FieldPreviewEnabledCheckBox.IsChecked == true,
                    FieldPreviewExpressionIndex = Math.Max(0, FieldPreviewExpressionComboBox.SelectedIndex),
                    FieldPreviewPalette = GetFieldPaletteName(),
                    ParameterStateA = new Dictionary<string, double>(_parameterStateA, StringComparer.OrdinalIgnoreCase),
                    ParameterStateB = new Dictionary<string, double>(_parameterStateB, StringComparer.OrdinalIgnoreCase),
                    ParameterStateBlend = ParameterStateBlendSlider.Value,
                    CurveKeys = CurveKeys.Select(key => new WorkspaceCurveKey
                    {
                        Id = key.Id, X = key.X, Y = key.Y, InTangent = key.InTangent, OutTangent = key.OutTangent,
                        InWeight = key.InWeight, OutWeight = key.OutWeight, LinkedTangents = key.LinkedTangents, TangentMode = key.TangentMode
                    }).ToList(),
                    CurveBeforeMode = _curveBeforeMode.ToString(),
                    CurveAfterMode = _curveAfterMode.ToString(),
                    CurveChannels = CurveChannels.Select(ToWorkspaceCurveChannel).ToList(),
                    ActiveCurveChannelId = _activeCurveChannel?.Id ?? Guid.Empty,
                    CurveAutoTension = _curveAutoTension,
                    SharedAssets = SharedAssets.Select(ToWorkspaceSharedAsset).ToList(),
                    ImportedTables = ImportedTables.Select(t => new WorkspaceImportedTable { Name = t.Name, XUnit = t.XUnit, YUnit = t.YUnit, Rows = t.Rows.ToList() }).ToList(),
                    EconomyTime = _economyTime,
                    EconomyTimeStep = ReadEconomyDt(),
                    EconomyPlaybackSpeed = TryEconomyDouble(EconomySpeedTextBox.Text, out double economySpeed) ? economySpeed : 1,
                    EconomySeed = ReadEconomySeed(),
                    EconomyPredictionRuns = int.TryParse(EconomyPredictionRunsTextBox.Text, out int predictionRuns) ? predictionRuns : 500,
                    EconomyPredictionHorizon = TryEconomyDouble(EconomyPredictionHorizonTextBox.Text, out double predictionHorizon) ? predictionHorizon : 30,
                    EconomyProjectName = EconomyProjectNameTextBox.Text,
                    EconomyProjectDescription = EconomyProjectDescriptionTextBox.Text,
                    EconomyNodes = EconomyNodes.Select(ToWorkspaceEconomyNode).ToList(),
                    EconomyLinks = EconomyLinks.Select(ToWorkspaceEconomyLink).ToList(),
                    EconomyParameters = EconomyParameters.Select(parameter => new WorkspaceEconomyParameter
                    {
                        Name = parameter.Name, Minimum = parameter.Minimum, Maximum = parameter.Maximum, Value = parameter.Value
                    }).ToList(),
                    EconomyScenarios = EconomyScenarios.Select(item => new WorkspaceEconomyScenario
                    {
                        Name = item.Name, ParameterValues = new Dictionary<string, double>(item.ParameterValues, StringComparer.OrdinalIgnoreCase)
                    }).ToList(),
                    EconomyCohorts = EconomyCohorts.Select(item => new WorkspaceEconomyCohort
                    {
                        Name = item.Name, Weight = item.Weight, ParameterValues = new Dictionary<string, double>(item.ParameterValues, StringComparer.OrdinalIgnoreCase)
                    }).ToList(),
                    EconomyTargets = EconomyTargets.Select(item => new WorkspaceEconomyTarget
                    {
                        NodeId = item.NodeId, NodeName = item.NodeName, Minimum = item.Minimum, Maximum = item.Maximum, Weight = item.Weight
                    }).ToList(),
                    EconomySubsystems = EconomySubsystems.Select(ToWorkspaceSubsystem).ToList(),
                    EconomyRecipes = EconomyRecipes.Select(ToWorkspaceRecipe).ToList(),
                    EconomyResourceStyles = EconomyResourceStyles.Select(ToWorkspaceResourceStyle).ToList(),
                    Expressions = Expressions.Select(item => new WorkspaceExpression
                    {
                        Expression = item.Expression,
                        IsVisible = item.IsVisible,
                        DomainMinX = item.DomainMinX,
                        DomainMaxX = item.DomainMaxX,
                        DomainMinY = item.DomainMinY,
                        DomainMaxY = item.DomainMaxY
                    }).ToList(),
                    Parameters = Parameters.Select(parameter => new WorkspaceParameter
                    {
                        Name = parameter.Name,
                        Minimum = parameter.Minimum,
                        Maximum = parameter.Maximum,
                        Value = parameter.Value,
                        IsAnimating = parameter.IsAnimating,
                        AnimationSpeed = parameter.AnimationSpeed,
                        AnimationMode = parameter.AnimationMode,
                        DisplayName = parameter.DisplayName,
                        Group = parameter.Group,
                        Unit = parameter.Unit,
                        IsLocked = parameter.IsLocked
                    }).ToList()
                };
        }

        private void SaveWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save graph workspace",
                Filter = "Graph Calculator workspace (*.graphcalc)|*.graphcalc|JSON file (*.json)|*.json",
                DefaultExt = ".graphcalc",
                AddExtension = true,
                FileName = "graph-workspace.graphcalc"
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                SaveActiveCurveChannel();
                var workspace = CaptureWorkspaceForSave();

                string json = JsonSerializer.Serialize(workspace, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dialog.FileName, json, Encoding.UTF8);
                _currentWorkspacePath = dialog.FileName;
                _projectDirty = false;
                UpdateProjectDirtyUi();
                RememberRecentWorkspace(dialog.FileName);
                GraphStatusText.Text = "Workspace saved";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not save workspace", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void ApplyWorkspaceFile(WorkspaceFile workspace, string path)
        {
            _loadingWorkspace = true;
            _animationTimer.Stop();
            try
            {
                ClearWorkspaceCollections();
                CurveKeys.Clear();
                ClearEconomyModel();
                SharedAssets.Clear(); ImportedTables.Clear(); CurveChannels.Clear(); EconomySubsystems.Clear(); EconomyRecipes.Clear(); EconomyResourceStyles.Clear();
            
                _viewport = workspace.PlotViewport.Width > 0 && workspace.PlotViewport.Height > 0
                    ? workspace.PlotViewport
                    : new PlotViewport(-10, 10, -10, 10);
                _surfaceViewport = workspace.SurfaceViewport.IsValid
                    ? workspace.SurfaceViewport
                    : new SurfaceViewport(-10, 10, -10, 10, -10, 10);
                _surfaceYaw = workspace.SurfaceYaw;
                _surfacePitch = Math.Clamp(workspace.SurfacePitch, -82, 82);
                _surfaceCameraDistance = Math.Clamp(workspace.SurfaceCameraDistance, 9, 55);
                _surfaceDisplayMode = Enum.TryParse(workspace.SurfaceDisplayMode, true, out SurfaceDisplayMode displayMode)
                    ? displayMode
                    : SurfaceDisplayMode.Solid;
                _timelineStart = double.IsFinite(workspace.TimelineStart) ? workspace.TimelineStart : 0;
                _timelineEnd = double.IsFinite(workspace.TimelineEnd) && workspace.TimelineEnd > _timelineStart ? workspace.TimelineEnd : _timelineStart + 10;
                _timelineTime = Math.Clamp(double.IsFinite(workspace.TimelineTime) ? workspace.TimelineTime : _timelineStart, _timelineStart, _timelineEnd);
                _timelineSpeed = double.IsFinite(workspace.TimelineSpeed) && workspace.TimelineSpeed > 0 ? workspace.TimelineSpeed : 1;
                _timelineLoop = workspace.TimelineLoop;
                _renderQuality = Enum.TryParse(workspace.RenderQuality, true, out RenderQuality loadedQuality) ? loadedQuality : RenderQuality.Normal;
                _timelinePlaying = false;
                _is3DMode = workspace.Is3DMode;
                PlotModeComboBox.SelectedIndex = _is3DMode ? 1 : 0;
            
                foreach (WorkspaceExpression source in workspace.Expressions)
                {
                    GraphExpression item = AddExpression(source.Expression);
                    item.IsVisible = source.IsVisible;
                    item.DomainMinX = source.DomainMinX ?? string.Empty;
                    item.DomainMaxX = source.DomainMaxX ?? string.Empty;
                    item.DomainMinY = source.DomainMinY ?? string.Empty;
                    item.DomainMaxY = source.DomainMaxY ?? string.Empty;
                }
            
                if (Expressions.Count == 0) AddExpression();
                SyncParameters();
            
                foreach (WorkspaceParameter source in workspace.Parameters)
                {
                    GraphParameter? parameter = Parameters.FirstOrDefault(candidate =>
                        candidate.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    if (parameter == null) continue;
            
                    parameter.SetRangeAndValue(source.Minimum, source.Maximum, source.Value);
                    parameter.DisplayName = string.IsNullOrWhiteSpace(source.DisplayName) ? parameter.Name : source.DisplayName;
                    parameter.Group = source.Group;
                    parameter.Unit = source.Unit;
                    parameter.AnimationSpeed = source.AnimationSpeed;
                    parameter.AnimationMode = source.AnimationMode;
                    parameter.IsAnimating = source.IsAnimating;
                    parameter.IsLocked = source.IsLocked;
                }
            
                SurfaceDisplayModeComboBox.SelectedIndex = _surfaceDisplayMode switch
                {
                    SurfaceDisplayMode.Wireframe => 1,
                    SurfaceDisplayMode.SolidWireframe => 2,
                    SurfaceDisplayMode.HeightBands => 3,
                    SurfaceDisplayMode.Slope => 4,
                    SurfaceDisplayMode.Normals => 5,
                    _ => 0
                };
                ShowDerivativeCheckBox.IsChecked = workspace.ShowDerivative;
                ComparisonEnabledCheckBox.IsChecked = workspace.ComparisonEnabled;
                ComparisonModeComboBox.SelectedIndex = workspace.ComparisonMode switch
                {
                    "|A - B|" => 1,
                    "A / B" => 2,
                    _ => 0
                };
                if (Expressions.Count > 0)
                {
                    ComparisonAComboBox.SelectedIndex = Math.Clamp(workspace.ComparisonAIndex, 0, Expressions.Count - 1);
                    ComparisonBComboBox.SelectedIndex = Math.Clamp(workspace.ComparisonBIndex, 0, Expressions.Count - 1);
                    CrossSectionExpressionComboBox.SelectedIndex = Math.Clamp(workspace.CrossSectionExpressionIndex, 0, Expressions.Count - 1);
                }
                CrossSectionEnabledCheckBox.IsChecked = workspace.CrossSectionEnabled;
                CrossSectionAxisComboBox.SelectedIndex = workspace.CrossSectionAxis.Equals("X", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                CrossSectionValueTextBox.Text = string.IsNullOrWhiteSpace(workspace.CrossSectionValue) ? "0" : workspace.CrossSectionValue;
                RenderQualityComboBox.SelectedIndex = _renderQuality switch { RenderQuality.Draft => 0, RenderQuality.High => 2, RenderQuality.Ultra => 3, _ => 1 };
                FieldPreviewEnabledCheckBox.IsChecked = workspace.FieldPreviewEnabled;
                FieldPreviewPaletteComboBox.SelectedIndex = workspace.FieldPreviewPalette switch { "Grayscale" => 0, "Heat" => 2, _ => 1 };
                if (Expressions.Count > 0) FieldPreviewExpressionComboBox.SelectedIndex = Math.Clamp(workspace.FieldPreviewExpressionIndex, 0, Expressions.Count - 1);
                _parameterStateA.Clear(); foreach (var pair in workspace.ParameterStateA ?? new Dictionary<string, double>()) _parameterStateA[pair.Key] = pair.Value;
                _parameterStateB.Clear(); foreach (var pair in workspace.ParameterStateB ?? new Dictionary<string, double>()) _parameterStateB[pair.Key] = pair.Value;
                ParameterStateBlendSlider.Value = Math.Clamp(workspace.ParameterStateBlend, 0, 1);
            
                _curveBeforeMode = Enum.TryParse(workspace.CurveBeforeMode, true, out CurveExtrapolationMode loadedBefore) ? loadedBefore : CurveExtrapolationMode.Clamp;
                _curveAfterMode = Enum.TryParse(workspace.CurveAfterMode, true, out CurveExtrapolationMode loadedAfter) ? loadedAfter : CurveExtrapolationMode.Clamp;
                SetCurveWrapCombo(CurveBeforeModeComboBox, _curveBeforeMode);
                SetCurveWrapCombo(CurveAfterModeComboBox, _curveAfterMode);
                _curveAutoTension = Math.Clamp(workspace.CurveAutoTension, 0, 1); CurveDesignerMath.AutoTension = _curveAutoTension;
                if (CurveTensionSlider != null) CurveTensionSlider.Value = _curveAutoTension;
                foreach (WorkspaceSharedAsset source in workspace.SharedAssets ?? []) SharedAssets.Add(FromWorkspaceSharedAsset(source));
                foreach (WorkspaceImportedTable source in workspace.ImportedTables ?? []) ImportedTables.Add(new ImportedTable { Name = source.Name, XUnit = source.XUnit, YUnit = source.YUnit, Rows = source.Rows?.ToList() ?? [] });
                foreach (WorkspaceCurveChannel source in workspace.CurveChannels ?? []) CurveChannels.Add(FromWorkspaceCurveChannel(source));
                _activeCurveChannel = CurveChannels.FirstOrDefault(c => c.Id == workspace.ActiveCurveChannelId) ?? CurveChannels.FirstOrDefault();
                if (_activeCurveChannel != null) CurveChannelsComboBox.SelectedItem = _activeCurveChannel;
            
                foreach (WorkspaceCurveKey source in workspace.CurveKeys ?? [])
                {
                    CurveKeys.Add(new CurveKey
                    {
                        Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
                        X = source.X, Y = source.Y, InTangent = source.InTangent, OutTangent = source.OutTangent,
                        InWeight = source.InWeight <= 0 ? 1 : source.InWeight, OutWeight = source.OutWeight <= 0 ? 1 : source.OutWeight,
                        LinkedTangents = source.LinkedTangents, TangentMode = source.TangentMode ?? "Auto"
                    });
                }
            
                if (CurveChannels.Count > 0) LoadActiveCurveChannel();
            
                EconomyProjectNameTextBox.Text = string.IsNullOrWhiteSpace(workspace.EconomyProjectName) ? "Economy model" : workspace.EconomyProjectName;
                EconomyProjectDescriptionTextBox.Text = workspace.EconomyProjectDescription ?? string.Empty;
                EconomyDtTextBox.Text = (workspace.EconomyTimeStep > 0 ? workspace.EconomyTimeStep : 0.1).ToString("G6", CultureInfo.InvariantCulture);
                EconomySpeedTextBox.Text = (workspace.EconomyPlaybackSpeed > 0 ? workspace.EconomyPlaybackSpeed : 1).ToString("G6", CultureInfo.InvariantCulture);
                EconomySeedTextBox.Text = workspace.EconomySeed.ToString(CultureInfo.InvariantCulture);
                EconomyPredictionRunsTextBox.Text = Math.Max(1, workspace.EconomyPredictionRuns).ToString(CultureInfo.InvariantCulture);
                EconomyPredictionHorizonTextBox.Text = (workspace.EconomyPredictionHorizon > 0 ? workspace.EconomyPredictionHorizon : 30).ToString("G6", CultureInfo.InvariantCulture);
            
                foreach (WorkspaceEconomyNode source in workspace.EconomyNodes ?? []) EconomyNodes.Add(FromWorkspaceEconomyNode(source));
                foreach (WorkspaceEconomyLink source in workspace.EconomyLinks ?? []) EconomyLinks.Add(FromWorkspaceEconomyLink(source));
                SyncEconomyParameters();
                foreach (WorkspaceEconomyParameter source in workspace.EconomyParameters ?? [])
                {
                    EconomyParameter? parameter = EconomyParameters.FirstOrDefault(p => p.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    if (parameter == null) continue;
                    parameter.Minimum = source.Minimum; parameter.Maximum = source.Maximum; parameter.Value = source.Value;
                }
                foreach (WorkspaceEconomyScenario source in workspace.EconomyScenarios ?? [])
                    EconomyScenarios.Add(new EconomyScenario { Name = source.Name, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
                foreach (WorkspaceEconomyCohort source in workspace.EconomyCohorts ?? [])
                    EconomyCohorts.Add(new EconomyCohort { Name = source.Name, Weight = source.Weight, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
                foreach (WorkspaceEconomyTarget source in workspace.EconomyTargets ?? [])
                    EconomyTargets.Add(new EconomyTarget { NodeId = source.NodeId, NodeName = source.NodeName, Minimum = source.Minimum, Maximum = source.Maximum, Weight = source.Weight });
                foreach (WorkspaceEconomySubsystem source in workspace.EconomySubsystems ?? []) EconomySubsystems.Add(FromWorkspaceSubsystem(source));
                foreach (WorkspaceEconomyRecipe source in workspace.EconomyRecipes ?? []) EconomyRecipes.Add(FromWorkspaceRecipe(source));
                foreach (WorkspaceEconomyResourceStyle source in workspace.EconomyResourceStyles ?? []) EconomyResourceStyles.Add(FromWorkspaceResourceStyle(source));
                if (EconomyScenarios.Count > 0) EconomyScenarioComboBox.SelectedIndex = 0;
                if (EconomyCohorts.Count > 0) EconomyCohortComboBox.SelectedIndex = 0;
                if (EconomyNodes.Count > 0) EconomyTargetNodeComboBox.SelectedIndex = 0;
            
                _economyTime = Math.Max(0, workspace.EconomyTime);
                _economyHistory.Clear();
                _economyPendingTransfers.Clear();
                _economyNextActivation.Clear();
                ResetEconomyRandomStreams();
                foreach (EconomyLink link in EconomyLinks) _economyNextActivation[link.Id] = Math.Max(link.StartTime, _economyTime);
                RecordEconomyHistory();
                UpdateTimelineUi();
            }
            finally
            {
                _loadingWorkspace = false;
            }
            
            WorkspaceMode loadedWorkspaceMode = Enum.TryParse(workspace.WorkspaceMode, true, out WorkspaceMode parsedWorkspaceMode)
                ? parsedWorkspaceMode
                : WorkspaceMode.FunctionLab;
            SetWorkspaceMode(loadedWorkspaceMode, updateCombo: true);
            RefreshParameterGroups();
            UpdatePlotRangeInputs();
            UpdateSurfaceRangeInputs();
            UpdatePlotModeUi(refreshExpressions: true);
            UpdateSurfaceCamera();
            UpdateAnimationTimerState();
            UpdateViewRangeText();
            GraphStatusText.Text = "Workspace loaded";
            _currentWorkspacePath = path;
            _projectDirty = false; UpdateProjectDirtyUi(); RememberRecentWorkspace(path);
            _undoStack.Clear();
            _redoStack.Clear();
            _pendingUndoBefore = null;
            _editBaseline = CaptureEditSnapshot();
            ScheduleRender(0);
        }

        private void LoadWorkspaceFromPath(string path)
        {
            WorkspaceFile? workspace = JsonSerializer.Deserialize<WorkspaceFile>(File.ReadAllText(path));
            if (workspace == null) throw new InvalidDataException("The workspace file is empty or invalid.");
            ApplyWorkspaceFile(workspace, path);
        }

        private void LoadWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open graph workspace",
                Filter = "Graph Calculator workspace (*.graphcalc;*.json)|*.graphcalc;*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                WorkspaceFile? workspace = JsonSerializer.Deserialize<WorkspaceFile>(File.ReadAllText(dialog.FileName));
                if (workspace == null) throw new InvalidDataException("The workspace file is empty or invalid.");

                ApplyWorkspaceFile(workspace, dialog.FileName);
            }
            catch (Exception ex)
            {
                _loadingWorkspace = false;
                MessageBox.Show(this, ex.Message, "Could not load workspace", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearWorkspaceCollections()
        {
            _renderCancellation?.Cancel();
            _fitCancellation?.Cancel();
            _animationTimer.Stop();
            _animationRenderPending = false;

            foreach (GraphExpression item in Expressions)
            {
                item.PropertyChanged -= Expression_PropertyChanged;
            }
            Expressions.Clear();

            foreach (GraphParameter parameter in Parameters)
            {
                parameter.PropertyChanged -= Parameter_PropertyChanged;
            }
            Parameters.Clear();

            _activeExpressionBox = null;
            _nextColorIndex = 0;
            ParametersExpander.Visibility = Visibility.Collapsed;
        }

        private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, double> parameterValues = GetParameterValues();
            List<SeriesRequest> active = GetActiveSeriesRequests(_is3DMode, parameterValues);
            if (active.Count == 0)
            {
                GraphStatusText.Text = "Nothing to export";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export sampled graph data",
                Filter = "CSV file (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = _is3DMode ? "graph-3d.csv" : "graph-2d.csv"
            };
            if (dialog.ShowDialog(this) != true) return;

            GraphStatusText.Text = "Sampling export…";
            try
            {
                string csv = await Task.Run(() => BuildCsvExport(active, parameterValues, _is3DMode));
                File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                GraphStatusText.Text = "CSV exported";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not export CSV", MessageBoxButton.OK, MessageBoxImage.Error);
                GraphStatusText.Text = "Export failed";
            }
        }

        private string BuildCsvExport(
            IReadOnlyList<SeriesRequest> active,
            Dictionary<string, double> parameterValues,
            bool threeDimensional)
        {
            var csv = new StringBuilder();
            csv.AppendLine("plot,kind,sample,x,y,z,vx,vy");

            using var cancellation = new CancellationTokenSource();
            foreach (SeriesRequest request in active)
            {
                string expression = EscapeCsv(request.Expression.Expression);
                int sampleIndex = 0;

                if (!threeDimensional && request.Kind == GraphExpressionKind.Scalar)
                {
                    List<GraphPoint> points = GraphSampler.Sample(
                        request.Compiled!, _viewport, 1400, 900, parameterValues,
                        request.Domain.MinX, request.Domain.MaxX, cancellation.Token);
                    foreach (GraphPoint point in points.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)))
                    {
                        csv.Append(expression).Append(",function,").Append(sampleIndex++).Append(',')
                            .Append(Invariant(point.X)).Append(',').Append(Invariant(point.Y)).Append(",,,").AppendLine();
                    }
                }
                else if (!threeDimensional && request.Kind == GraphExpressionKind.Parametric2D)
                {
                    List<GraphPoint> points = ParametricCurveSampler.Sample2D(
                        request.Components, parameterValues, request.Domain.MinX, request.Domain.MaxX, 1600, cancellation.Token);
                    foreach (GraphPoint point in points.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y)))
                    {
                        csv.Append(expression).Append(",parametric,").Append(sampleIndex++).Append(',')
                            .Append(Invariant(point.X)).Append(',').Append(Invariant(point.Y)).Append(",,,").AppendLine();
                    }
                }
                else if (!threeDimensional && request.Kind == GraphExpressionKind.VectorField2D)
                {
                    List<VectorFieldPoint> points = VectorFieldSampler.Sample(
                        request.Components, _viewport, parameterValues,
                        request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                        28, 20, cancellation.Token);
                    foreach (VectorFieldPoint point in points)
                    {
                        csv.Append(expression).Append(",vector-field,").Append(sampleIndex++).Append(',')
                            .Append(Invariant(point.X)).Append(',').Append(Invariant(point.Y)).Append(",,")
                            .Append(Invariant(point.VX)).Append(',').Append(Invariant(point.VY)).AppendLine();
                    }
                }
                else if (!threeDimensional && request.Kind == GraphExpressionKind.Implicit2D)
                {
                    List<GraphSegment> segments = ImplicitSampler.Sample2D(
                        request.Components[0], _viewport, parameterValues,
                        request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                        220, cancellation.Token);
                    foreach (GraphSegment segment in segments)
                    {
                        foreach (GraphPoint point in new[] { segment.A, segment.B })
                        {
                            csv.Append(expression).Append(",implicit-contour,").Append(sampleIndex++).Append(',')
                                .Append(Invariant(point.X)).Append(',').Append(Invariant(point.Y)).Append(",,,").AppendLine();
                        }
                    }
                }
                else if (!threeDimensional && request.Kind == GraphExpressionKind.TextureField2D)
                {
                    double minX = Math.Max(_viewport.MinX, request.Domain.MinX ?? _viewport.MinX);
                    double maxX = Math.Min(_viewport.MaxX, request.Domain.MaxX ?? _viewport.MaxX);
                    double minY = Math.Max(_viewport.MinY, request.Domain.MinY ?? _viewport.MinY);
                    double maxY = Math.Min(_viewport.MaxY, request.Domain.MaxY ?? _viewport.MaxY);
                    const int res = 96;
                    for (int row = 0; row < res; row++)
                    {
                        double y = minY + (maxY - minY) * row / (res - 1.0);
                        for (int column = 0; column < res; column++)
                        {
                            double x = minX + (maxX - minX) * column / (res - 1.0);
                            double value;
                            try { value = request.Components[0].Evaluate(x, y, parameterValues); }
                            catch { continue; }
                            if (!double.IsFinite(value)) continue;
                            csv.Append(expression).Append(",texture-field,").Append(sampleIndex++).Append(',')
                                .Append(Invariant(x)).Append(',').Append(Invariant(y)).Append(',')
                                .Append(Invariant(value)).Append(",,").AppendLine();
                        }
                    }
                }
                else if (threeDimensional && request.Kind == GraphExpressionKind.Parametric3D)
                {
                    List<GraphPoint3D> points = ParametricCurveSampler.Sample3D(
                        request.Components, parameterValues, request.Domain.MinX, request.Domain.MaxX, 1200, cancellation.Token);
                    foreach (GraphPoint3D point in points.Where(ParametricSurfaceSampler.IsFinite))
                    {
                        csv.Append(expression).Append(",parametric3d,").Append(sampleIndex++).Append(',')
                            .Append(Invariant(point.X)).Append(',').Append(Invariant(point.Y)).Append(',')
                            .Append(Invariant(point.Z)).Append(",,").AppendLine();
                    }
                }
                else if (threeDimensional && request.Kind == GraphExpressionKind.ParametricSurface3D)
                {
                    ParametricSurfaceSample sample = ParametricSurfaceSampler.Sample(
                        request.Components, parameterValues,
                        request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                        70, cancellation.Token);
                    foreach (GraphPoint3D point in sample.Points.Where(ParametricSurfaceSampler.IsFinite))
                    {
                        csv.Append(expression).Append(",parametric-surface,").Append(sampleIndex++).Append(',')
                            .Append(Invariant(point.X)).Append(',').Append(Invariant(point.Y)).Append(',')
                            .Append(Invariant(point.Z)).Append(",,").AppendLine();
                    }
                }
                else if (threeDimensional && request.Kind == GraphExpressionKind.Implicit3D)
                {
                    ImplicitSurfaceSample sample = ImplicitSampler.Sample3D(
                        request.Components[0], _surfaceViewport, parameterValues,
                        request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                        24, cancellation.Token);
                    foreach (GraphPoint3D point in sample.Vertices)
                    {
                        csv.Append(expression).Append(",implicit-surface,").Append(sampleIndex++).Append(',')
                            .Append(Invariant(point.X)).Append(',').Append(Invariant(point.Y)).Append(',')
                            .Append(Invariant(point.Z)).Append(",,").AppendLine();
                    }
                }
                else if (threeDimensional && request.Kind == GraphExpressionKind.Scalar)
                {
                    double minX = Math.Max(_surfaceViewport.MinX, request.Domain.MinX ?? _surfaceViewport.MinX);
                    double maxX = Math.Min(_surfaceViewport.MaxX, request.Domain.MaxX ?? _surfaceViewport.MaxX);
                    double minY = Math.Max(_surfaceViewport.MinY, request.Domain.MinY ?? _surfaceViewport.MinY);
                    double maxY = Math.Min(_surfaceViewport.MaxY, request.Domain.MaxY ?? _surfaceViewport.MaxY);
                    const int resolution = 70;

                    for (int row = 0; row < resolution; row++)
                    {
                        double y = minY + (maxY - minY) * row / (resolution - 1.0);
                        for (int column = 0; column < resolution; column++)
                        {
                            double x = minX + (maxX - minX) * column / (resolution - 1.0);
                            double z;
                            try { z = request.Compiled!.Evaluate(x, y, parameterValues); }
                            catch { continue; }
                            if (!double.IsFinite(z)) continue;
                            csv.Append(expression).Append(",surface,").Append(sampleIndex++).Append(',')
                                .Append(Invariant(x)).Append(',').Append(Invariant(y)).Append(',')
                                .Append(Invariant(z)).Append(",,").AppendLine();
                        }
                    }
                }
            }

            return csv.ToString();
        }

        private static string EscapeCsv(string value)
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private static string Invariant(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private void PlotModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace) return;

            _is3DMode = PlotModeComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
                && tag.Equals("3D", StringComparison.OrdinalIgnoreCase);

            UpdatePlotModeUi(refreshExpressions: true);
            UpdateViewRangeText();
            ScheduleRender(0);
        }

        private void UpdatePlotModeUi(bool refreshExpressions)
        {
            PlotCanvas.Visibility = _is3DMode ? Visibility.Collapsed : Visibility.Visible;
            OverlayCanvas.Visibility = _is3DMode ? Visibility.Collapsed : Visibility.Visible;
            SurfaceViewport3D.Visibility = _is3DMode ? Visibility.Visible : Visibility.Collapsed;
            PlotRangePanel.Visibility = _is3DMode ? Visibility.Collapsed : Visibility.Visible;
            SurfaceRangePanel.Visibility = _is3DMode ? Visibility.Visible : Visibility.Collapsed;
            SurfaceLegend.Visibility = _is3DMode ? Visibility.Visible : Visibility.Collapsed;
            TwoDToolsPanel.Visibility = _is3DMode ? Visibility.Collapsed : Visibility.Visible;
            ThreeDToolsPanel.Visibility = _is3DMode ? Visibility.Visible : Visibility.Collapsed;
            if (_is3DMode)
            {
                FieldPreviewImage.Visibility = Visibility.Collapsed;
                AnalysisResultsPanel.Visibility = Visibility.Collapsed;
            }

            foreach (GraphExpression item in Expressions) item.ShowYDomain = _is3DMode;

            InteractionHintText.Text = _is3DMode
                ? "Scroll to zoom · drag to orbit · right-click to probe"
                : "Scroll to zoom · drag to pan";

            ExamplesText.Text = _is3DMode
                ? "Try: surface((2+0.7*cos(v))*cos(u),(2+0.7*cos(v))*sin(u),0.7*sin(v)), implicit3(x^2+y^2+z^2-9), curve3(3*cos(t),3*sin(t),t/3)"
                : "Try: if(x<0,0,x^2), implicit(x^2+y^2-9), texture(fbm(x,y,4,0.5,2)), field(-y,x)";

            FooterHintText.Text = _is3DMode
                ? "surface(...) draws parametric geometry · implicit3(f) draws f=0 isosurfaces · right-click probes mesh normals"
                : "Ctrl+Space suggests functions · implicit(f) traces f=0 · texture(f) previews 2D fields · comparisons support piecewise math";

            HideHover();
            if (!_is3DMode)
            {
                SurfaceProbePanel.Visibility = Visibility.Collapsed;
                CrossSectionPreview.Visibility = Visibility.Collapsed;
            }

            if (refreshExpressions)
            {
                foreach (GraphExpression item in Expressions) RefreshExpression(item);
                SyncParameters();
                RefreshAllExpressionStatuses();
            }
        }

        private async Task RenderSurfaceGraphAsync()
        {
            double width = GraphSurface.ActualWidth;
            double height = GraphSurface.ActualHeight;
            if (width < 20 || height < 20) return;

            _renderCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _renderCancellation = cancellation;
            CancellationToken token = cancellation.Token;

            Dictionary<string, double> parameterValues = GetParameterValues();
            List<SeriesRequest> active = GetActiveSeriesRequests(threeDimensional: true, parameterValues);
            List<SeriesRequest> surfaces = active.Where(request => request.Kind == GraphExpressionKind.Scalar).ToList();
            List<SeriesRequest> curves = active.Where(request => request.Kind == GraphExpressionKind.Parametric3D).ToList();
            List<SeriesRequest> parametricSurfaces = active.Where(request => request.Kind == GraphExpressionKind.ParametricSurface3D).ToList();
            List<SeriesRequest> implicitSurfaces = active.Where(request => request.Kind == GraphExpressionKind.Implicit3D).ToList();

            SurfaceViewport viewport = _surfaceViewport;
            int resolution = GetSurfaceResolution(width, height);

            try
            {
                var samples = await Task.Run(() =>
                {
                    var scalarSamples = new List<SurfaceSample>(surfaces.Count);
                    foreach (SeriesRequest request in surfaces)
                    {
                        token.ThrowIfCancellationRequested();
                        scalarSamples.Add(SurfaceSampler.Sample(
                            request.Compiled!, viewport, resolution, parameterValues,
                            request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY, token));
                    }

                    var curveSamples = new List<List<GraphPoint3D>>(curves.Count);
                    foreach (SeriesRequest request in curves)
                    {
                        token.ThrowIfCancellationRequested();
                        curveSamples.Add(ParametricCurveSampler.Sample3D(
                            request.Components, parameterValues, request.Domain.MinX, request.Domain.MaxX, GetParametricCurveSampleCount(true), token));
                    }

                    var parametricSurfaceSamples = new List<ParametricSurfaceSample>(parametricSurfaces.Count);
                    foreach (SeriesRequest request in parametricSurfaces)
                    {
                        token.ThrowIfCancellationRequested();
                        parametricSurfaceSamples.Add(ParametricSurfaceSampler.Sample(
                            request.Components, parameterValues,
                            request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                            resolution, token));
                    }

                    var implicitSurfaceSamples = new List<ImplicitSurfaceSample>(implicitSurfaces.Count);
                    foreach (SeriesRequest request in implicitSurfaces)
                    {
                        token.ThrowIfCancellationRequested();
                        implicitSurfaceSamples.Add(ImplicitSampler.Sample3D(
                            request.Components[0], viewport, parameterValues,
                            request.Domain.MinX, request.Domain.MaxX, request.Domain.MinY, request.Domain.MaxY,
                            GetImplicit3DResolution(), token));
                    }

                    return (Scalar: scalarSamples, Curves: curveSamples, Parametric: parametricSurfaceSamples, Implicit: implicitSurfaceSamples);
                }, token);

                token.ThrowIfCancellationRequested();

                var surfaceLayers = new List<SurfaceRenderLayer>(surfaces.Count);
                for (int i = 0; i < surfaces.Count; i++)
                {
                    surfaceLayers.Add(new SurfaceRenderLayer(samples.Scalar[i], surfaces[i].Expression.Color));
                }

                var parametricSurfaceLayers = new List<ParametricSurfaceRenderLayer>(parametricSurfaces.Count);
                for (int i = 0; i < parametricSurfaces.Count; i++)
                {
                    parametricSurfaceLayers.Add(new ParametricSurfaceRenderLayer(samples.Parametric[i], parametricSurfaces[i].Expression.Color));
                }

                var implicitSurfaceLayers = new List<ImplicitSurfaceRenderLayer>(implicitSurfaces.Count);
                for (int i = 0; i < implicitSurfaces.Count; i++)
                {
                    implicitSurfaceLayers.Add(new ImplicitSurfaceRenderLayer(samples.Implicit[i], implicitSurfaces[i].Expression.Color));
                }

                var curveLayers = new List<ParametricCurveRenderLayer>(curves.Count + 1);
                for (int i = 0; i < curves.Count; i++)
                {
                    curveLayers.Add(new ParametricCurveRenderLayer(samples.Curves[i], curves[i].Expression.Color));
                }

                if (TryBuildCrossSection(parameterValues, out List<GraphPoint3D> section3D, out List<GraphPoint> section2D, out Brush sectionColor, out string sectionTitle))
                {
                    curveLayers.Add(new ParametricCurveRenderLayer(section3D, sectionColor, 0.04));
                    DrawCrossSectionPreview(section2D, sectionColor, sectionTitle);
                }
                else
                {
                    CrossSectionPreview.Visibility = Visibility.Collapsed;
                }

                SurfaceSceneVisual.Content = SurfaceSceneBuilder.Build(
                    surfaceLayers, parametricSurfaceLayers, implicitSurfaceLayers, curveLayers, viewport, _surfaceDisplayMode);

                GraphStatusText.Text = active.Count == 0
                    ? "No visible 3D plots"
                    : $"{surfaces.Count} height fields · {parametricSurfaces.Count} param surfaces · {implicitSurfaces.Count} implicit · {curves.Count} curves";
                UpdateViewRangeText();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_renderCancellation, cancellation)) _renderCancellation = null;
                cancellation.Dispose();
                FlushPendingAnimationRender();
            }
        }

        private void ApplyPlotRangeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadPlotRange(out PlotViewport viewport))
            {
                GraphStatusText.Text = "Invalid 2D range";
                return;
            }

            _viewport = viewport;
            UpdatePlotRangeInputs();
            UpdateViewRangeText();
            ScheduleRender(0);
        }

        private bool TryReadPlotRange(out PlotViewport viewport)
        {
            viewport = default;
            Dictionary<string, double> values = GetParameterValues();

            if (!TryEvaluateScalar(PlotMinXTextBox.Text, values, out double minX, out _)
                || !TryEvaluateScalar(PlotMaxXTextBox.Text, values, out double maxX, out _)
                || !TryEvaluateScalar(PlotMinYTextBox.Text, values, out double minY, out _)
                || !TryEvaluateScalar(PlotMaxYTextBox.Text, values, out double maxY, out _))
            {
                return false;
            }

            if (!(minX < maxX) || !(minY < maxY)) return false;
            viewport = new PlotViewport(minX, maxX, minY, maxY);
            return true;
        }

        private void UpdatePlotRangeInputs()
        {
            if (PlotMinXTextBox == null) return;

            PlotMinXTextBox.Text = NumberFormatting.FormatAxis(_viewport.MinX);
            PlotMaxXTextBox.Text = NumberFormatting.FormatAxis(_viewport.MaxX);
            PlotMinYTextBox.Text = NumberFormatting.FormatAxis(_viewport.MinY);
            PlotMaxYTextBox.Text = NumberFormatting.FormatAxis(_viewport.MaxY);
        }

        private void ApplySurfaceRangeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadSurfaceRange(out SurfaceViewport viewport))
            {
                GraphStatusText.Text = "Invalid 3D range";
                return;
            }

            _surfaceViewport = viewport;
            UpdateSurfaceRangeInputs();
            UpdateViewRangeText();
            ScheduleRender(0);
        }

        private bool TryReadSurfaceRange(out SurfaceViewport viewport)
        {
            viewport = default;
            Dictionary<string, double> values = GetParameterValues();

            if (!TryEvaluateScalar(SurfaceMinXTextBox.Text, values, out double minX, out _)
                || !TryEvaluateScalar(SurfaceMaxXTextBox.Text, values, out double maxX, out _)
                || !TryEvaluateScalar(SurfaceMinYTextBox.Text, values, out double minY, out _)
                || !TryEvaluateScalar(SurfaceMaxYTextBox.Text, values, out double maxY, out _)
                || !TryEvaluateScalar(SurfaceMinZTextBox.Text, values, out double minZ, out _)
                || !TryEvaluateScalar(SurfaceMaxZTextBox.Text, values, out double maxZ, out _))
            {
                return false;
            }

            viewport = new SurfaceViewport(minX, maxX, minY, maxY, minZ, maxZ);
            return viewport.IsValid;
        }

        private void UpdateSurfaceRangeInputs()
        {
            if (SurfaceMinXTextBox == null) return;

            SurfaceMinXTextBox.Text = NumberFormatting.FormatAxis(_surfaceViewport.MinX);
            SurfaceMaxXTextBox.Text = NumberFormatting.FormatAxis(_surfaceViewport.MaxX);
            SurfaceMinYTextBox.Text = NumberFormatting.FormatAxis(_surfaceViewport.MinY);
            SurfaceMaxYTextBox.Text = NumberFormatting.FormatAxis(_surfaceViewport.MaxY);
            SurfaceMinZTextBox.Text = NumberFormatting.FormatAxis(_surfaceViewport.MinZ);
            SurfaceMaxZTextBox.Text = NumberFormatting.FormatAxis(_surfaceViewport.MaxZ);
        }

        private List<SeriesRequest> GetActiveSeriesRequests(
            bool threeDimensional,
            Dictionary<string, double> parameterValues)
        {
            var result = new List<SeriesRequest>();

            foreach (GraphExpression item in Expressions)
            {
                if (!item.IsVisible || !item.CompiledParts.Any() || string.IsNullOrWhiteSpace(item.Expression)) continue;

                bool compatible = threeDimensional
                    ? item.Kind is GraphExpressionKind.Scalar or GraphExpressionKind.Parametric3D or GraphExpressionKind.ParametricSurface3D or GraphExpressionKind.Implicit3D
                    : item.Kind is GraphExpressionKind.Scalar or GraphExpressionKind.Parametric2D or GraphExpressionKind.VectorField2D or GraphExpressionKind.Implicit2D or GraphExpressionKind.TextureField2D;
                if (!compatible) continue;

                if (!threeDimensional && item.Kind == GraphExpressionKind.Scalar && item.Compiled?.DependsOnY == true) continue;

                if ((item.Kind is GraphExpressionKind.Parametric2D or GraphExpressionKind.Parametric3D)
                    && item.Components.Any(component => component.DependsOnX || component.DependsOnY))
                {
                    continue;
                }

                if (item.Kind == GraphExpressionKind.ParametricSurface3D
                    && item.Components.Any(component => component.DependsOnX || component.DependsOnY))
                {
                    continue;
                }

                if (!TryReadExpressionDomain(item, parameterValues, out ExpressionDomain domain, out string? error, threeDimensional))
                {
                    item.StatusText = "Range: " + error;
                    continue;
                }

                result.Add(new SeriesRequest(item, item.Kind, item.Compiled, item.Components, domain));
            }

            return result;
        }

        private bool TryReadExpressionDomain(
            GraphExpression item,
            IDictionary<string, double> parameterValues,
            out ExpressionDomain domain,
            out string? error,
            bool? includeYOverride = null)
        {
            domain = default;
            error = null;

            bool includeY = item.Kind is GraphExpressionKind.ParametricSurface3D or GraphExpressionKind.VectorField2D or GraphExpressionKind.Implicit2D or GraphExpressionKind.Implicit3D or GraphExpressionKind.TextureField2D
                || ((includeYOverride ?? _is3DMode) && item.Kind == GraphExpressionKind.Scalar);

            if (!TryReadOptionalScalar(item.DomainMinX, parameterValues, out double? minX, out error)
                || !TryReadOptionalScalar(item.DomainMaxX, parameterValues, out double? maxX, out error))
            {
                return false;
            }

            if (minX.HasValue && maxX.HasValue && minX.Value >= maxX.Value)
            {
                string axis = item.Kind switch
                {
                    GraphExpressionKind.Parametric2D or GraphExpressionKind.Parametric3D => "t",
                    GraphExpressionKind.ParametricSurface3D => "u",
                    _ => "x"
                };
                error = $"{axis} minimum must be less than {axis} maximum";
                return false;
            }

            double? minY = null;
            double? maxY = null;
            if (includeY)
            {
                if (!TryReadOptionalScalar(item.DomainMinY, parameterValues, out minY, out error)
                    || !TryReadOptionalScalar(item.DomainMaxY, parameterValues, out maxY, out error))
                {
                    return false;
                }

                if (minY.HasValue && maxY.HasValue && minY.Value >= maxY.Value)
                {
                    string axis = item.Kind == GraphExpressionKind.ParametricSurface3D ? "v" : "y";
                    error = $"{axis} minimum must be less than {axis} maximum";
                    return false;
                }
            }

            domain = new ExpressionDomain(minX, maxX, minY, maxY);
            return true;
        }

        private static bool TryReadOptionalScalar(
            string text,
            IDictionary<string, double> variables,
            out double? value,
            out string? error)
        {
            value = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text)) return true;

            if (!TryEvaluateScalar(text, variables, out double parsed, out error))
            {
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryEvaluateScalar(
            string text,
            IDictionary<string, double> variables,
            out double value,
            out string? error)
        {
            value = default;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "range value is empty";
                return false;
            }

            if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                && double.IsFinite(value))
            {
                return true;
            }

            try
            {
                CalculatorEngine.CompiledExpression expression = CalculatorEngine.Compile(ExpandSharedAssets(text));
                if (expression.DependsOnX || expression.DependsOnY)
                {
                    error = "range bounds cannot depend on x or y";
                    return false;
                }

                value = expression.Evaluate(variables);
                if (!double.IsFinite(value))
                {
                    error = "range value is not finite";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void SurfaceViewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _surfaceIsDragging = true;
            _surfaceDragStart = e.GetPosition(SurfaceViewport3D);
            _surfaceDragStartYaw = _surfaceYaw;
            _surfaceDragStartPitch = _surfacePitch;
            SurfaceViewport3D.CaptureMouse();
            SurfaceViewport3D.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void SurfaceViewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_surfaceIsDragging) return;

            _surfaceIsDragging = false;
            SurfaceViewport3D.ReleaseMouseCapture();
            SurfaceViewport3D.Cursor = Cursors.Arrow;
            e.Handled = true;
        }

        private void SurfaceViewport3D_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_surfaceIsDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                if (_surfaceIsDragging && e.LeftButton != MouseButtonState.Pressed)
                {
                    _surfaceIsDragging = false;
                    SurfaceViewport3D.ReleaseMouseCapture();
                    SurfaceViewport3D.Cursor = Cursors.Arrow;
                }

                return;
            }

            Point point = e.GetPosition(SurfaceViewport3D);
            double dx = point.X - _surfaceDragStart.X;
            double dy = point.Y - _surfaceDragStart.Y;

            _surfaceYaw = _surfaceDragStartYaw + dx * 0.35;
            _surfacePitch = Math.Clamp(_surfaceDragStartPitch - dy * 0.3, -82, 82);
            UpdateSurfaceCamera();
        }

        private void SurfaceViewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _surfaceCameraDistance *= e.Delta > 0 ? 0.88 : 1.14;
            _surfaceCameraDistance = Math.Clamp(_surfaceCameraDistance, 9, 55);
            UpdateSurfaceCamera();
            e.Handled = true;
        }

        private void UpdateSurfaceCamera()
        {
            if (SurfaceCamera == null) return;

            double yaw = _surfaceYaw * Math.PI / 180.0;
            double pitch = _surfacePitch * Math.PI / 180.0;
            double horizontal = Math.Cos(pitch) * _surfaceCameraDistance;

            var position = new Point3D(
                Math.Sin(yaw) * horizontal,
                Math.Sin(pitch) * _surfaceCameraDistance,
                Math.Cos(yaw) * horizontal);

            SurfaceCamera.Position = position;
            SurfaceCamera.LookDirection = new Vector3D(-position.X, -position.Y, -position.Z);
            SurfaceCamera.UpDirection = new Vector3D(0, 1, 0);
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

        private readonly record struct ExpressionDomain(
            double? MinX,
            double? MaxX,
            double? MinY,
            double? MaxY);

        private sealed record SeriesRequest(
            GraphExpression Expression,
            GraphExpressionKind Kind,
            CalculatorEngine.CompiledExpression? Compiled,
            IReadOnlyList<CalculatorEngine.CompiledExpression> Components,
            ExpressionDomain Domain);
    }
}
