using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private readonly DispatcherTimer _economyTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly DispatcherTimer _economyVisualTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Stopwatch _economyClock = Stopwatch.StartNew();
        private double _economyLastClock;
        private double _economyTime;
        private double _economyHistoryAccumulator;
        private bool _economyPlaying;
        private EconomyNode? _selectedEconomyNode;
        private EconomyLink? _selectedEconomyLink;
        private EconomyNode? _economyConnectionStart;
        private EconomyNode? _dragEconomyNode;
        private Point _dragEconomyPointerStart;
        private double _dragEconomyNodeX;
        private double _dragEconomyNodeY;
        private readonly List<EconomyHistoryPoint> _economyHistory = [];
        private readonly Dictionary<Guid, double> _economyLastFlowRates = [];
        private readonly List<EconomyFlowVisualPulse> _economyVisualPulses = [];
        private readonly Dictionary<Guid, double> _economyVisualPendingAmount = [];
        private readonly Dictionary<Guid, double> _economyVisualLastSpawn = [];
        private readonly HashSet<Guid> _economyHistoryVisibleSeries = [];
        private bool _economyHistorySelectionTouched;
        private bool _updatingEconomyHistorySelector;
        private bool _updatingEconomyInspector;
        private bool _updatingEconomySimulation;
        private readonly Dictionary<Guid, EconomyDeterministicRandom> _economyLinkRandoms = [];
        private readonly Dictionary<Guid, double> _economyNextActivation = [];
        private readonly List<EconomyPendingTransfer> _economyPendingTransfers = [];
        private Dictionary<string, double>? _lastOptimizationValues;

        private static readonly HashSet<string> EconomyReservedVariables = new(StringComparer.OrdinalIgnoreCase)
        {
            "t", "time", "dt", "source", "target", "sourcecap", "targetcap", "rand", "gauss", "run", "cohort"
        };

        // Visual packets are deliberately separate from the simulation state. A packet is born only
        // after a real transfer was accepted, then it gets enough screen-time to actually be readable.
        private sealed class EconomyFlowVisualPulse
        {
            public required Guid LinkId { get; init; }
            public required double StartedAt { get; init; }
            public required double Duration { get; init; }
            public required bool UsesSimulationTime { get; init; }
            public required double Amount { get; init; }
            public required int PacketCount { get; init; }
        }

        private void InitializeEconomyDesigner()
        {
            EconomyNodes.CollectionChanged += EconomyNodes_CollectionChanged;
            EconomyLinks.CollectionChanged += EconomyLinks_CollectionChanged;
            EconomyParameters.CollectionChanged += EconomyParameters_CollectionChanged;
            _economyTimer.Tick += EconomyTimer_Tick;
            _economyVisualTimer.Tick += EconomyVisualTimer_Tick;
            _economyVisualTimer.Start();
            EconomyScenarioComboBox.ItemsSource = EconomyScenarios;
            EconomyCohortComboBox.ItemsSource = EconomyCohorts;
            EconomyTargetNodeComboBox.ItemsSource = EconomyNodes;
            EconomyTargetsListBox.ItemsSource = EconomyTargets;
            SyncEconomyHistorySeriesSelector();
            UpdateEconomyInspector();
        }

        private void EconomyNodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (EconomyNode n in e.OldItems) n.PropertyChanged -= EconomyNode_PropertyChanged;
            if (e.NewItems != null) foreach (EconomyNode n in e.NewItems) n.PropertyChanged += EconomyNode_PropertyChanged;
            SyncEconomyHistorySeriesSelector();
            if (!_updatingEconomySimulation) MarkWorkspaceEdit();
            RenderEconomyDiagram();
            RenderEconomyChart();
        }

        private void EconomyLinks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (EconomyLink link in e.OldItems) link.PropertyChanged -= EconomyLink_PropertyChanged;
            if (e.NewItems != null)
            {
                foreach (EconomyLink link in e.NewItems)
                {
                    link.PropertyChanged += EconomyLink_PropertyChanged;
                    CompileEconomyLink(link);
                }
            }
            SyncEconomyParameters();
            if (!_updatingEconomySimulation) MarkWorkspaceEdit();
            RenderEconomyDiagram();
        }

        private void EconomyParameters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (EconomyParameter p in e.OldItems) p.PropertyChanged -= EconomyParameter_PropertyChanged;
            if (e.NewItems != null) foreach (EconomyParameter p in e.NewItems) p.PropertyChanged += EconomyParameter_PropertyChanged;
        }

        private void EconomyNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_updatingEconomySimulation) return;
            if (_dragEconomyNode != null && ReferenceEquals(sender, _dragEconomyNode) && (e.PropertyName is nameof(EconomyNode.X) or nameof(EconomyNode.Y))) return;

            if (sender is EconomyNode renamed && e.PropertyName == nameof(EconomyNode.Name))
            {
                foreach (EconomyTarget target in EconomyTargets.Where(t => t.NodeId == renamed.Id))
                    target.NodeName = renamed.Name;
                EconomyTargetsListBox?.Items.Refresh();
            }

            MarkWorkspaceEdit();
            if (_workspaceMode == WorkspaceMode.EconomyDesigner) RenderEconomyDiagram();
        }

        private void EconomyLink_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_updatingEconomySimulation) return;
            if (sender is EconomyLink link && e.PropertyName is nameof(EconomyLink.RateExpression) or nameof(EconomyLink.ConditionExpression))
            {
                CompileEconomyLink(link);
                SyncEconomyParameters();
            }
            if (sender is EconomyLink scheduled && e.PropertyName is nameof(EconomyLink.Interval) or nameof(EconomyLink.StartTime))
                _economyNextActivation[scheduled.Id] = Math.Max(_economyTime, scheduled.StartTime);
            if (_workspaceMode == WorkspaceMode.EconomyDesigner) RenderEconomyDiagram();
        }

        private void EconomyParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_workspaceMode == WorkspaceMode.EconomyDesigner) UpdateEconomyDiagnostics();
        }

        private void EconomyAddSourceButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Source);
        private void EconomyAddEventButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Event);
        private void EconomyAddPoolButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Pool);
        private void EconomyAddGateButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Gate);
        private void EconomyAddQueueButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Queue);
        private void EconomyAddRegisterButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Register);
        private void EconomyAddConverterButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Converter);
        private void EconomyAddActionButton_Click_Compat(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Action);
        private void EconomyAddSinkButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Sink);

        private void AddEconomyNode(EconomyNodeKind kind)
        {
            int n = EconomyNodes.Count + 1;
            var node = new EconomyNode
            {
                Kind = kind,
                Name = kind switch
                {
                    EconomyNodeKind.Source => $"Source {n}",
                    EconomyNodeKind.Event => $"Event {n}",
                    EconomyNodeKind.Pool => $"Pool {n}",
                    EconomyNodeKind.Gate => $"Gate {n}",
                    EconomyNodeKind.Queue => $"Queue {n}",
                    EconomyNodeKind.Register => $"Register {n}",
                    EconomyNodeKind.Converter => $"Converter {n}",
                    EconomyNodeKind.Action => $"Action {n}",
                    EconomyNodeKind.Sink => $"Sink {n}",
                    _ => $"Node {n}"
                },
                Resource = kind == EconomyNodeKind.Register ? "Value" : (kind == EconomyNodeKind.Action ? "Action" : "Resource"),
                X = 50 + (n % 4) * 155,
                Y = 55 + (n / 4) * 105,
                InitialAmount = kind is EconomyNodeKind.Pool or EconomyNodeKind.Register ? 100 : 0,
                Amount = kind is EconomyNodeKind.Pool or EconomyNodeKind.Register ? 100 : 0,
                Capacity = EconomyNode.StoresAmount(kind)
                    ? (kind == EconomyNodeKind.Gate ? 1e12 : 1000)
                    : double.PositiveInfinity
            };
            EconomyNodes.Add(node);
            SelectEconomyNode(node);
        }

        private void EconomyDeleteNodeButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = _economySelectedNodeIds.Count > 0
                ? _economySelectedNodeIds.ToHashSet()
                : (_selectedEconomyNode != null ? new HashSet<Guid> { _selectedEconomyNode.Id } : new HashSet<Guid>());
            if (ids.Count == 0) return;

            foreach (EconomyLink link in EconomyLinks.Where(l => ids.Contains(l.SourceId) || ids.Contains(l.TargetId)).ToList())
                EconomyLinks.Remove(link);
            foreach (EconomyNode node in EconomyNodes.Where(n => ids.Contains(n.Id)).ToList())
                EconomyNodes.Remove(node);

            foreach (EconomyRecipe recipe in EconomyRecipes.Where(r => ids.Contains(r.ConverterNodeId)).ToList())
                EconomyRecipes.Remove(recipe);

            _economySelectedNodeIds.Clear();
            _selectedEconomyNode = null;
            _selectedEconomyLink = null;
            EconomyLinksListBox.SelectedItem = null;
            UpdateEconomyInspector();
            RenderEconomyDiagram();
            MarkWorkspaceEdit();
        }

        private void EconomyDeleteFlowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEconomyLink == null) return;
            EconomyLinks.Remove(_selectedEconomyLink);
            _selectedEconomyLink = null;
            EconomyLinksListBox.SelectedItem = null;
            UpdateEconomyInspector();
        }

        private void EconomyConnectToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _economyConnectionStart = null;
            EconomyConnectHintText.Text = EconomyConnectToggle.IsChecked == true
                ? "Connection mode: click an origin node, then a target node."
                : "Drag nodes to arrange them.";
        }

        private void EconomyNodeMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { Tag: EconomyNode node } border) return;
            e.Handled = true;

            if (EconomyConnectToggle.IsChecked == true)
            {
                if (_economyConnectionStart == null)
                {
                    if (node.Kind == EconomyNodeKind.Sink)
                    {
                        EconomyConnectHintText.Text = "A sink cannot be the start of a flow.";
                        return;
                    }
                    _economyConnectionStart = node;
                    EconomyConnectHintText.Text = $"From {node.Name} → choose a target";
                    SelectEconomyNode(node);
                    return;
                }

                if (ReferenceEquals(_economyConnectionStart, node)) return;
                if (node.IsGenerator)
                {
                    EconomyConnectHintText.Text = "A Source/Event cannot be the target of a resource flow.";
                    return;
                }
                AddEconomyLink(_economyConnectionStart, node, "1", 1);
                _economyConnectionStart = null;
                EconomyConnectHintText.Text = "Flow created. Choose another source, or leave Connect mode.";
                return;
            }

            EconomySelectNodeForMultiEdit(node);
            _dragEconomyNode = node;
            _dragEconomyPointerStart = e.GetPosition(EconomyCanvas);
            _dragEconomyNodeX = node.X;
            _dragEconomyNodeY = node.Y;
            border.CaptureMouse();
        }

        private void EconomyNodeMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragEconomyNode == null || e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(EconomyCanvas);
            _dragEconomyNode.X = Math.Max(0, _dragEconomyNodeX + p.X - _dragEconomyPointerStart.X);
            _dragEconomyNode.Y = Math.Max(0, _dragEconomyNodeY + p.Y - _dragEconomyPointerStart.Y);
            if (sender is Border border)
            {
                Canvas.SetLeft(border, _dragEconomyNode.X);
                Canvas.SetTop(border, _dragEconomyNode.Y);
            }
        }

        private void EconomyNodeMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragEconomyNode == null) return;
            if (sender is Border border) border.ReleaseMouseCapture();
            _dragEconomyNode = null;
            MarkWorkspaceEdit();
            RenderEconomyDiagram();
            e.Handled = true;
        }

        private void EconomyCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource != EconomyCanvas) return;
            _selectedEconomyNode = null;
            _selectedEconomyLink = null;
            EconomyLinksListBox.SelectedItem = null;
            UpdateEconomyInspector();
            RenderEconomyDiagram();
        }

        private void EconomyCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderEconomyDiagram();
        private void EconomyChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderEconomyChart();

        private void SelectEconomyNode(EconomyNode node)
        {
            _selectedEconomyNode = node;
            _selectedEconomyLink = null;
            EconomyLinksListBox.SelectedItem = null;
            UpdateEconomyInspector();
            RenderEconomyDiagram();
        }

        private void EconomyLinksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedEconomyLink = EconomyLinksListBox.SelectedItem as EconomyLink;
            UpdateEconomyInspector();
            RenderEconomyDiagram();
        }

        private void EconomyNodeInspector_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_updatingEconomyInspector || _selectedEconomyNode == null) return;
            _selectedEconomyNode.Name = string.IsNullOrWhiteSpace(EconomyNodeNameTextBox.Text) ? _selectedEconomyNode.Name : EconomyNodeNameTextBox.Text.Trim();
            _selectedEconomyNode.Resource = EconomyNodeResourceTextBox.Text;
            _selectedEconomyNode.Notes = EconomyNodeNotesTextBox.Text;
            if (_selectedEconomyNode.IsStorage)
            {
                if (TryEconomyDouble(EconomyNodeInitialTextBox.Text, out double initial)) _selectedEconomyNode.InitialAmount = Math.Max(0, initial);
                if (TryEconomyDouble(EconomyNodeCapacityTextBox.Text, out double cap)) _selectedEconomyNode.Capacity = Math.Max(0.0001, cap);
            }
            RenderEconomyDiagram();
        }
        private void EconomyRateTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingEconomyInspector || _selectedEconomyLink == null) return;
            _selectedEconomyLink.RateExpression = EconomyRateTextBox.Text;
            CompileEconomyLink(_selectedEconomyLink);
            SyncEconomyParameters();
            EconomyFlowStatusText.Text = _selectedEconomyLink.Status;
        }

        private void EconomyFlowInspector_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_updatingEconomyInspector || _selectedEconomyLink == null) return;
            _selectedEconomyLink.Label = EconomyFlowLabelTextBox.Text?.Trim() ?? string.Empty;
            _selectedEconomyLink.ConditionExpression = string.IsNullOrWhiteSpace(EconomyConditionTextBox.Text) ? "1" : EconomyConditionTextBox.Text;
            if (TryEconomyDouble(EconomyEfficiencyTextBox.Text, out double efficiency)) _selectedEconomyLink.Efficiency = Math.Clamp(efficiency, 0, 100);
            if (TryEconomyDouble(EconomyChanceTextBox.Text, out double chance)) _selectedEconomyLink.Chance = Math.Clamp(chance, 0, 1);
            if (TryEconomyDouble(EconomyIntervalTextBox.Text, out double interval)) _selectedEconomyLink.Interval = Math.Max(0, interval);
            if (TryEconomyDouble(EconomyDelayTextBox.Text, out double delay)) _selectedEconomyLink.Delay = Math.Max(0, delay);
            if (TryEconomyDouble(EconomyStartTextBox.Text, out double start)) _selectedEconomyLink.StartTime = Math.Max(0, start);
            if (string.IsNullOrWhiteSpace(EconomyEndTextBox.Text) || EconomyEndTextBox.Text.Trim() == "0") _selectedEconomyLink.EndTime = double.PositiveInfinity;
            else if (TryEconomyDouble(EconomyEndTextBox.Text, out double end)) _selectedEconomyLink.EndTime = Math.Max(_selectedEconomyLink.StartTime, end);
            CompileEconomyLink(_selectedEconomyLink);
            SyncEconomyParameters();
            RenderEconomyDiagram();
        }
        private void EconomyFlowEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingEconomyInspector || _selectedEconomyLink == null) return;
            _selectedEconomyLink.IsEnabled = EconomyFlowEnabledCheckBox.IsChecked == true;
        }

        private void UpdateEconomyInspector()
        {
            if (EconomyNodeNameTextBox == null) return;
            _updatingEconomyInspector = true;
            try
            {
                if (_selectedEconomyNode != null)
                {
                    EconomyNodeNameTextBox.Text = _selectedEconomyNode.Name;
                    EconomyNodeResourceTextBox.Text = _selectedEconomyNode.Resource;
                    EconomyNodeNotesTextBox.Text = _selectedEconomyNode.Notes;
                    EconomyNodeInitialTextBox.Text = NumberFormatting.Format(_selectedEconomyNode.InitialAmount);
                    EconomyNodeCapacityTextBox.Text = double.IsFinite(_selectedEconomyNode.Capacity) ? NumberFormatting.Format(_selectedEconomyNode.Capacity) : "∞";
                    EconomyNodeInitialTextBox.IsEnabled = _selectedEconomyNode.IsStorage;
                    EconomyNodeCapacityTextBox.IsEnabled = _selectedEconomyNode.IsStorage;
                    EconomyNodeKindHelpText.Text = EconomyNodeKindHelp(_selectedEconomyNode.Kind);
                }
                else
                {
                    EconomyNodeNameTextBox.Text = string.Empty;
                    EconomyNodeResourceTextBox.Text = string.Empty;
                    EconomyNodeNotesTextBox.Text = string.Empty;
                    EconomyNodeInitialTextBox.Text = string.Empty;
                    EconomyNodeCapacityTextBox.Text = string.Empty;
                    EconomyNodeInitialTextBox.IsEnabled = EconomyNodeCapacityTextBox.IsEnabled = false;
                    EconomyNodeKindHelpText.Text = "Select a node to see what role it plays in the simulation.";
                }

                if (_selectedEconomyLink != null)
                {
                    EconomyFlowLabelTextBox.Text = _selectedEconomyLink.Label;
                    EconomyRateTextBox.Text = _selectedEconomyLink.RateExpression;
                    EconomyConditionTextBox.Text = _selectedEconomyLink.ConditionExpression;
                    EconomyEfficiencyTextBox.Text = NumberFormatting.Format(_selectedEconomyLink.Efficiency);
                    EconomyChanceTextBox.Text = NumberFormatting.Format(_selectedEconomyLink.Chance);
                    EconomyIntervalTextBox.Text = NumberFormatting.Format(_selectedEconomyLink.Interval);
                    EconomyDelayTextBox.Text = NumberFormatting.Format(_selectedEconomyLink.Delay);
                    EconomyStartTextBox.Text = NumberFormatting.Format(_selectedEconomyLink.StartTime);
                    EconomyEndTextBox.Text = double.IsFinite(_selectedEconomyLink.EndTime) ? NumberFormatting.Format(_selectedEconomyLink.EndTime) : string.Empty;
                    EconomyFlowEnabledCheckBox.IsChecked = _selectedEconomyLink.IsEnabled;
                    EconomyFlowStatusText.Text = _selectedEconomyLink.Status;
                }
                else
                {
                    EconomyFlowLabelTextBox.Text = string.Empty;
                    EconomyRateTextBox.Text = string.Empty;
                    EconomyConditionTextBox.Text = string.Empty;
                    EconomyEfficiencyTextBox.Text = string.Empty;
                    EconomyChanceTextBox.Text = string.Empty;
                    EconomyIntervalTextBox.Text = string.Empty;
                    EconomyDelayTextBox.Text = string.Empty;
                    EconomyStartTextBox.Text = string.Empty;
                    EconomyEndTextBox.Text = string.Empty;
                    EconomyFlowEnabledCheckBox.IsChecked = false;
                    EconomyFlowStatusText.Text = string.Empty;
                }
            }
            finally { _updatingEconomyInspector = false; }
        }
        private static string EconomyNodeKindHelp(EconomyNodeKind kind) => kind switch
        {
            EconomyNodeKind.Source => "Source: creates the named resource. Its outgoing flow expressions decide how much is generated.",
            EconomyNodeKind.Event => "Event: source-like, but intended for scheduled, chance-based or limited-time output. Configure timing on its outgoing flows.",
            EconomyNodeKind.Pool => "Pool: stores a resource between 0 and its capacity. Most wallets, inventories and stockpiles are Pools.",
            EconomyNodeKind.Gate => "Gate: routing buffer. Use conditions/chances on its outgoing flows to branch the same resource into different paths.",
            EconomyNodeKind.Queue => "Queue: aggregate holding buffer for paced release. Put interval/delay rules on its outgoing flows; it is not a per-item FIFO queue.",
            EconomyNodeKind.Register => "Register: numeric state stored like a Pool. Useful for reputation, pressure, counters or other abstract values that influence formulas.",
            EconomyNodeKind.Converter => "Converter: processing/storage stage for changing one resource into another. It can also own an atomic multi-resource recipe.",
            EconomyNodeKind.Action => "Action: a player or designer-triggered operation. Trigger it manually or drive it from replay/debug workflows; its outgoing flows execute only when queued.",
            EconomyNodeKind.Sink => "Sink: consumes the named resource and records how much has been spent or destroyed.",
            _ => string.Empty
        };

        private EconomyLink AddEconomyLink(EconomyNode source, EconomyNode target, string rate, double efficiency)
        {
            var link = new EconomyLink { SourceId = source.Id, TargetId = target.Id, RateExpression = rate, Efficiency = efficiency };
            EconomyLinks.Add(link);
            _selectedEconomyLink = link;
            EconomyLinksListBox.SelectedItem = link;
            UpdateEconomyInspector();
            return link;
        }

        private void CompileEconomyLink(EconomyLink link)
        {
            try
            {
                link.CompiledRate = CalculatorEngine.Compile(ExpandSharedAssets(string.IsNullOrWhiteSpace(link.RateExpression) ? "0" : link.RateExpression));
                link.CompiledCondition = CalculatorEngine.Compile(ExpandSharedAssets(string.IsNullOrWhiteSpace(link.ConditionExpression) ? "1" : link.ConditionExpression));
                link.Status = "Ready";
            }
            catch (Exception ex)
            {
                link.CompiledRate = null;
                link.CompiledCondition = null;
                link.Status = "Error: " + ex.Message;
            }
        }
        private void SyncEconomyParameters()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EconomyLink link in EconomyLinks)
            {
                foreach (CalculatorEngine.CompiledExpression? compiled in new[] { link.CompiledRate, link.CompiledCondition })
                {
                    if (compiled == null) continue;
                    foreach (string name in compiled.Variables)
                        if (!EconomyReservedVariables.Contains(name)) names.Add(name);
                }
            }

            for (int i = EconomyParameters.Count - 1; i >= 0; i--)
                if (!names.Contains(EconomyParameters[i].Name)) EconomyParameters.RemoveAt(i);

            foreach (string name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (EconomyParameters.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                var parameter = new EconomyParameter(name);
                (double min, double max, double value) = name.ToLowerInvariant() switch
                {
                    "price" or "cost" or "upgradecost" => (0, 5000, 100),
                    "income" or "baseincome" or "reward" => (0, 500, 10),
                    "spend" or "basespend" or "craftcost" => (0, 500, 8),
                    "orerate" or "smeltrate" or "transfercap" => (0, 100, 12),
                    "saverate" or "spendrate" => (0, 100, 15),
                    "savetarget" or "surplusthreshold" => (0, 1000, 100),
                    "growth" or "inflation" or "dropchance" => (0, 1, 0.05),
                    "players" or "playercount" => (1, 100000, 1000),
                    _ => (0, 10, 1)
                };
                parameter.Minimum = min; parameter.Maximum = max; parameter.Value = value;
                EconomyParameters.Add(parameter);
            }
        }
        private EconomyDeterministicRandom GetEconomyLinkRandom(Guid linkId)
        {
            if (_economyLinkRandoms.TryGetValue(linkId, out EconomyDeterministicRandom? random)) return random;
            random = new EconomyDeterministicRandom(EconomyDeterministicRandom.DeriveSeed(ReadEconomySeed(), linkId));
            _economyLinkRandoms[linkId] = random;
            return random;
        }

        private void ResetEconomyRandomStreams()
        {
            _economyLinkRandoms.Clear();
        }

        private Dictionary<string, double> BuildEconomyVariables(EconomyNode source, EconomyNode target, double dt, double time, EconomyDeterministicRandom random)
        {
            var vars = EconomyParameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            vars["t"] = vars["time"] = time;
            vars["dt"] = dt;
            vars["source"] = source.Amount;
            vars["target"] = target.Amount;
            vars["sourcecap"] = double.IsFinite(source.Capacity) ? source.Capacity : 1e12;
            vars["targetcap"] = double.IsFinite(target.Capacity) ? target.Capacity : 1e12;
            vars["rand"] = random.NextDouble();
            vars["gauss"] = random.NextNormal();
            vars["run"] = 0;
            vars["cohort"] = EconomyCohortComboBox.SelectedIndex < 0 ? 0 : EconomyCohortComboBox.SelectedIndex;
            return vars;
        }
        private void EconomyPlayButton_Click(object sender, RoutedEventArgs e)
        {
            _economyPlaying = true;
            _economyLastClock = _economyClock.Elapsed.TotalSeconds;
            _economyTimer.Start();
        }

        private void EconomyPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _economyPlaying = false;
            _economyTimer.Stop();
        }

        private void EconomyStepButton_Click(object sender, RoutedEventArgs e)
        {
            EconomyPauseButton_Click(sender, e);
            EconomySimulationStep(ReadEconomyDt());
        }

        private void EconomyResetButton_Click(object sender, RoutedEventArgs e) => ResetEconomySimulation();

        private void EconomyVisualTimer_Tick(object? sender, EventArgs e)
        {
            if (_economyPlaying || _workspaceMode != WorkspaceMode.EconomyDesigner || !IsLoaded) return;
            double now = _economyClock.Elapsed.TotalSeconds;
            bool animateImmediate = _economyVisualPulses.Any(p => !p.UsesSimulationTime && EconomyPulseProgress(p, now) <= 1.25);
            _economyVisualPulses.RemoveAll(p => EconomyPulseProgress(p, now) > 1.25);
            if (animateImmediate) RenderEconomyDiagram();
        }

        private double EconomyPulseProgress(EconomyFlowVisualPulse pulse, double realNow)
        {
            double elapsed = pulse.UsesSimulationTime ? _economyTime - pulse.StartedAt : realNow - pulse.StartedAt;
            return elapsed / Math.Max(0.05, pulse.Duration);
        }

        private void EconomyTimer_Tick(object? sender, EventArgs e)
        {
            if (!_economyPlaying) return;
            double now = _economyClock.Elapsed.TotalSeconds;
            double realDt = Math.Clamp(now - _economyLastClock, 0, 0.12);
            _economyLastClock = now;
            double speed = TryEconomyDouble(EconomySpeedTextBox.Text, out double s) ? Math.Clamp(s, 0.05, 20) : 1;
            double remaining = realDt * speed;
            double fixedStep = ReadEconomyDt();
            int guard = 0;
            while (remaining > 1e-8 && guard++ < 50)
            {
                double step = Math.Min(fixedStep, remaining);
                EconomySimulationStep(step, render: false);
                remaining -= step;
            }
            RenderEconomyDiagram();
            RenderEconomyChart();
            UpdateEconomyDiagnostics();
        }

        private double ReadEconomyDt() => TryEconomyDouble(EconomyDtTextBox.Text, out double dt) ? Math.Clamp(dt, 0.001, 5) : 0.1;

        private long ReadEconomySeed() => long.TryParse(EconomySeedTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed) ? seed : 1337;
        private void ResetEconomySimulation()
        {
            _economyPlaying = false;
            _economyTimer.Stop();
            _economyTime = 0;
            _economyHistoryAccumulator = 0;
            _economyHistory.Clear();
            _economyLastFlowRates.Clear();
            _economyVisualPulses.Clear();
            _economyVisualPendingAmount.Clear();
            _economyVisualLastSpawn.Clear();
            _economyPendingTransfers.Clear();
            _economyNextActivation.Clear();
            ResetEconomyRandomStreams();
            foreach (EconomyLink link in EconomyLinks) _economyNextActivation[link.Id] = Math.Max(0, link.StartTime);
            foreach (EconomyNode node in EconomyNodes)
                node.Amount = node.IsStorage ? Math.Clamp(node.InitialAmount, 0, node.Capacity) : 0;
            RecordEconomyHistory();
            RenderEconomyDiagram();
            RenderEconomyChart();
            UpdateEconomyDiagnostics();
        }
        private void EconomySimulationStep(double dt, bool render = true)
        {
            if (dt <= 0 || EconomyNodes.Count == 0) return;
            _updatingEconomySimulation = true;
            try
            {
                var nodes = EconomyNodes.ToDictionary(n => n.Id);
                double stepEnd = _economyTime + dt;

                DeliverLivePendingThrough(_economyTime, dt, nodes);
                ProcessEconomyRecipes(dt);

                var snapshot = EconomyNodes.ToDictionary(n => n.Id, n => n.Amount);
                var proposals = new List<(EconomyLink link, EconomyNode source, EconomyNode target, double requested, double dueTime)>();
                var requestedByLink = new Dictionary<Guid, double>();

                foreach (EconomyLink link in EconomyLinks.Where(l => l.IsEnabled && l.CompiledRate != null && l.CompiledCondition != null))
                {
                    if (!nodes.TryGetValue(link.SourceId, out EconomyNode? source) || !nodes.TryGetValue(link.TargetId, out EconomyNode? target)) continue;
                    if (source.Kind == EconomyNodeKind.Action && !source.ActionQueued) continue;
                    if (stepEnd + 1e-9 < link.StartTime) continue;
                    if (double.IsFinite(link.EndTime) && _economyTime >= link.EndTime - 1e-9) continue;

                    EconomyDeterministicRandom random = GetEconomyLinkRandom(link.Id);

                    void EvaluateActivation(double eventTime, double span)
                    {
                        try
                        {
                            Dictionary<string, double> vars = BuildEconomyVariables(source, target, span, eventTime, random);
                            vars["source"] = snapshot[source.Id];
                            vars["target"] = snapshot[target.Id];

                            double condition = link.CompiledCondition!.Evaluate(vars);
                            if (!double.IsFinite(condition) || condition <= 0)
                            {
                                if (ReferenceEquals(link, _selectedEconomyLink) || _economyBreakpointLinkId == link.Id)
                                    RecordEconomyDebug(link, 0, 0, 0, "blocked by condition", condition, double.NaN, conditionPassed: false, chancePassed: true);
                                return;
                            }
                            double chanceRoll = link.Chance < 1 ? random.NextDouble() : double.NaN;
                            if (link.Chance < 1 && chanceRoll > link.Chance)
                            {
                                if (ReferenceEquals(link, _selectedEconomyLink) || _economyBreakpointLinkId == link.Id)
                                    RecordEconomyDebug(link, 0, 0, 0, "chance roll missed", condition, chanceRoll, conditionPassed: true, chancePassed: false);
                                return;
                            }

                            vars["rand"] = random.NextDouble();
                            vars["gauss"] = random.NextNormal();
                            double rate = link.CompiledRate!.Evaluate(vars);
                            if (!double.IsFinite(rate) || rate <= 0) return;

                            double requested = rate * span;
                            proposals.Add((link, source, target, requested, eventTime + link.Delay));
                            requestedByLink[link.Id] = requestedByLink.GetValueOrDefault(link.Id) + requested;
                        }
                        catch (Exception ex)
                        {
                            link.Status = "Runtime: " + ex.Message;
                        }
                    }

                    if (link.Interval > 1e-9)
                    {
                        if (!_economyNextActivation.TryGetValue(link.Id, out double next)) next = Math.Max(0, link.StartTime);
                        int activationGuard = 0;
                        while (next <= stepEnd + 1e-9 && activationGuard++ < 100000)
                        {
                            double activationTime = next;
                            next += link.Interval;
                            if (activationTime < _economyTime - 1e-9 || activationTime + 1e-9 < link.StartTime) continue;
                            if (double.IsFinite(link.EndTime) && activationTime >= link.EndTime - 1e-9) break;
                            EvaluateActivation(activationTime, link.Interval);
                        }
                        _economyNextActivation[link.Id] = next;
                    }
                    else
                    {
                        double activeStart = Math.Max(_economyTime, link.StartTime);
                        double activeEnd = double.IsFinite(link.EndTime) ? Math.Min(stepEnd, link.EndTime) : stepEnd;
                        double span = activeEnd - activeStart;
                        if (span > 1e-12) EvaluateActivation(activeStart, span);
                    }

                    if (requestedByLink.TryGetValue(link.Id, out double requestedThisStep))
                    {
                        double effectiveRate = requestedThisStep / Math.Max(dt, 1e-12);
                        _economyLastFlowRates[link.Id] = effectiveRate;
                        link.Status = link.Delay > 0
                            ? $"{NumberFormatting.Format(effectiveRate)} / s · +{NumberFormatting.Format(link.Delay)}s"
                            : $"{NumberFormatting.Format(effectiveRate)} / s";
                    }
                    else if (!link.Status.StartsWith("Runtime:", StringComparison.Ordinal))
                    {
                        _economyLastFlowRates[link.Id] = 0;
                        link.Status = "Idle";
                    }
                }

                // Scarce storage is shared proportionally. Canvas order must never become a hidden priority system.
                foreach (var group in proposals.Where(p => p.source.IsStorage).GroupBy(p => p.source.Id))
                {
                    double total = group.Sum(p => p.requested);
                    double available = snapshot[group.Key];
                    if (total <= available || total <= 1e-12) continue;
                    double scale = available / total;
                    for (int i = 0; i < proposals.Count; i++)
                        if (proposals[i].source.Id == group.Key)
                        {
                            var p = proposals[i];
                            proposals[i] = (p.link, p.source, p.target, p.requested * scale, p.dueTime);
                        }
                }

                foreach (var group in proposals.Where(p => p.link.Delay <= 1e-9 && p.target.IsStorage).GroupBy(p => p.target.Id))
                {
                    double room = Math.Max(0, group.First().target.Capacity - snapshot[group.Key]);
                    double delivered = group.Sum(p => p.requested * p.link.Efficiency);
                    if (delivered <= room || delivered <= 1e-12) continue;
                    double scale = room / delivered;
                    for (int i = 0; i < proposals.Count; i++)
                        if (proposals[i].target.Id == group.Key && proposals[i].link.Delay <= 1e-9)
                        {
                            var p = proposals[i];
                            proposals[i] = (p.link, p.source, p.target, p.requested * scale, p.dueTime);
                        }
                }

                foreach (EconomyLink link in EconomyLinks.Where(l => l.IsEnabled))
                {
                    if (!requestedByLink.TryGetValue(link.Id, out double rawRequested)) continue;
                    double actualRequested = proposals.Where(p => p.link.Id == link.Id).Sum(p => p.requested);
                    double rawRate = rawRequested / Math.Max(dt, 1e-12);
                    double actualRate = actualRequested / Math.Max(dt, 1e-12);
                    _economyLastFlowRates[link.Id] = actualRate;
                    string limited = actualRate + 1e-9 < rawRate ? $" · limited from {NumberFormatting.Format(rawRate)}/s" : string.Empty;
                    string delay = link.Delay > 0 ? $" · +{NumberFormatting.Format(link.Delay)}s" : string.Empty;
                    link.Status = $"{NumberFormatting.Format(actualRate)} / s{limited}{delay}";
                }

                foreach (var group in proposals.GroupBy(p => p.link.Id))
                {
                    EconomyLink flow = group.First().link;
                    double moved = group.Sum(p => p.requested);
                    if (moved > 1e-10)
                    {
                        RegisterEconomyVisualTransfer(flow, moved);
                        double raw = requestedByLink.GetValueOrDefault(flow.Id);
                        RecordEconomyDebug(flow, raw, moved, moved * flow.Efficiency, moved + 1e-9 < raw ? "limited by availability/capacity" : "accepted");
                    }
                }

                var delta = EconomyNodes.ToDictionary(n => n.Id, _ => 0.0);
                foreach (var p in proposals)
                {
                    double taken = p.requested;
                    double delivered = taken * p.link.Efficiency;
                    if (p.source.IsStorage) delta[p.source.Id] -= taken;
                    else if (p.source.IsGenerator) delta[p.source.Id] += taken;

                    if (p.link.Delay > 1e-9)
                    {
                        _economyPendingTransfers.Add(new EconomyPendingTransfer(p.link.Id, p.target.Id, p.dueTime, delivered));
                    }
                    else if (p.target.IsStorage) delta[p.target.Id] += delivered;
                    else if (p.target.Kind == EconomyNodeKind.Sink) delta[p.target.Id] += delivered;
                }

                foreach (EconomyNode node in EconomyNodes)
                {
                    if (node.IsStorage)
                        node.Amount = Math.Clamp(snapshot[node.Id] + delta[node.Id], 0, node.Capacity);
                    else
                        node.Amount = Math.Max(0, snapshot[node.Id] + delta[node.Id]);
                }

                foreach (EconomyNode action in EconomyNodes.Where(n => n.Kind == EconomyNodeKind.Action)) action.ActionQueued = false;
                DeliverLivePendingThrough(stepEnd, dt, nodes);
                _economyTime = stepEnd;
                _economyHistoryAccumulator += dt;
                if (_economyHistoryAccumulator >= 0.08)
                {
                    _economyHistoryAccumulator = 0;
                    RecordEconomyHistory();
                }
            }
            finally
            {
                _updatingEconomySimulation = false;
            }

            if (render)
            {
                RenderEconomyDiagram();
                RenderEconomyChart();
                UpdateEconomyDiagnostics();
            }
        }

        private void DeliverLivePendingThrough(double throughTime, double retryStep, IReadOnlyDictionary<Guid, EconomyNode> nodes)
        {
            for (int i = _economyPendingTransfers.Count - 1; i >= 0; i--)
            {
                EconomyPendingTransfer transfer = _economyPendingTransfers[i];
                if (transfer.DueTime > throughTime + 1e-9) continue;
                if (!nodes.TryGetValue(transfer.TargetId, out EconomyNode? target)) { _economyPendingTransfers.RemoveAt(i); continue; }

                double left = transfer.Amount;
                if (target.IsStorage)
                {
                    double room = Math.Max(0, target.Capacity - target.Amount);
                    double delivered = Math.Min(room, left);
                    target.Amount += delivered;
                    left -= delivered;
                    if (left > 1e-10)
                    {
                        _economyPendingTransfers[i] = transfer with
                        {
                            DueTime = throughTime + Math.Max(0.001, retryStep),
                            Amount = left
                        };
                        continue;
                    }
                }
                else if (target.Kind == EconomyNodeKind.Sink)
                {
                    target.Amount += left;
                }
                _economyPendingTransfers.RemoveAt(i);
            }
        }
        private void RecordEconomyHistory()
        {
            _economyHistory.Add(new EconomyHistoryPoint(_economyTime, EconomyNodes.ToDictionary(n => n.Id, n => n.Amount)));
            if (_economyHistory.Count > 1600) _economyHistory.RemoveRange(0, _economyHistory.Count - 1600);
        }

        private void RenderEconomyDiagram()
        {
            if (!IsLoaded || EconomyCanvas == null || _workspaceMode != WorkspaceMode.EconomyDesigner) return;
            EconomyCanvas.Children.Clear();
            RenderEconomyResourceLegend();
            DrawEconomySubsystemFrames();
            const double nodeWidth = 136;
            const double nodeHeight = 80;

            foreach (EconomyLink link in EconomyLinks)
            {
                EconomyNode? source = EconomyNodes.FirstOrDefault(n => n.Id == link.SourceId);
                EconomyNode? target = EconomyNodes.FirstOrDefault(n => n.Id == link.TargetId);
                if (source == null || target == null) continue;
                if (EconomyLinkIsInternalToCollapsedSubsystem(source, target)) continue;

                TryGetEconomyVisualAnchor(source, out double x1, out double y1, out string visualSourceName);
                TryGetEconomyVisualAnchor(target, out double x2, out double y2, out string visualTargetName);
                Brush sourceBrush = EconomyResourceBrush(source.Resource);
                Brush targetBrush = EconomyResourceBrush(target.Resource);
                double actualRate = _economyLastFlowRates.TryGetValue(link.Id, out double measured) ? Math.Max(0, measured) : 0;
                bool recipeVisual = IsEconomyRecipeLink(link);
                double visualTraffic = Math.Max(actualRate, EconomyVisualTraffic(link.Id));
                double thickness = 1.5 + Math.Min(3.5, Math.Log10(1 + visualTraffic) * 0.9);
                double opacity = (link.IsEnabled || recipeVisual) ? 0.92 : 0.28;

                bool conversionFlow = !source.Resource.Equals(target.Resource, StringComparison.OrdinalIgnoreCase);
                AddEconomyResourceArrow(x1, y1, x2, y2, sourceBrush, targetBrush, thickness, opacity, ReferenceEquals(link, _selectedEconomyLink), link.Delay > 1e-9, conversionFlow);
                AddEconomyFlowHitTarget(link, x1, y1, x2, y2, thickness);
                if (link.IsEnabled || recipeVisual)
                    AddEconomyFlowPackets(link, x1, y1, x2, y2, sourceBrush, targetBrush);

                string rate = _economyLastFlowRates.ContainsKey(link.Id)
                    ? NumberFormatting.Format(actualRate) + "/s"
                    : link.RateExpression;
                string resource = source.Resource.Equals(target.Resource, StringComparison.OrdinalIgnoreCase)
                    ? source.Resource
                    : $"{source.Resource} → {target.Resource}";
                string timing = link.Interval > 0 ? $" · every {NumberFormatting.Format(link.Interval)}s" : string.Empty;
                string chance = link.Chance < 0.999999 ? $" · {NumberFormatting.Format(link.Chance * 100)}%" : string.Empty;
                string delay = link.Delay > 0 ? $" · +{NumberFormatting.Format(link.Delay)}s" : string.Empty;
                var label = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(link.Label)
                        ? $"{resource} · {rate}{timing}{chance}{delay}\n{source.Name} → {target.Name}"
                        : $"{link.Label} · {resource} · {rate}{timing}{chance}{delay}\n{source.Name} → {target.Name}",
                    FontSize = 9.5,
                    Foreground = (link.IsEnabled || recipeVisual) ? Brushes.DimGray : Brushes.DarkGray,
                    Background = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)),
                    Padding = new Thickness(3, 1, 3, 1),
                    MaxWidth = 300,
                    TextWrapping = TextWrapping.Wrap,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(label, (x1 + x2) * 0.5 + 4);
                Canvas.SetTop(label, (y1 + y2) * 0.5 - 19);
                EconomyCanvas.Children.Add(label);
            }

            foreach (EconomyNode node in EconomyNodes)
            {
                if (IsEconomyNodeHidden(node)) continue;
                Brush kindBrush = EconomyVisuals.KindBrush(node.Kind);
                Brush resourceBrush = EconomyResourceBrush(node.Resource);
                string subtitle = node.Kind switch
                {
                    EconomyNodeKind.Source => $"{node.Resource} · generated {NumberFormatting.Format(node.Amount)}",
                    EconomyNodeKind.Event => $"{node.Resource} · event output {NumberFormatting.Format(node.Amount)}",
                    EconomyNodeKind.Sink => $"{node.Resource} · consumed {NumberFormatting.Format(node.Amount)}",
                    EconomyNodeKind.Gate => $"{node.Resource} · routed buffer {NumberFormatting.Format(node.Amount)}",
                    EconomyNodeKind.Queue => $"{node.Resource} · queued {NumberFormatting.Format(node.Amount)}",
                    EconomyNodeKind.Register => $"{node.Resource} · value {NumberFormatting.Format(node.Amount)}",
                    EconomyNodeKind.Action => node.ActionQueued ? "Action · queued" : "Action · idle",
                    _ => double.IsFinite(node.Capacity)
                        ? $"{node.Resource} · {NumberFormatting.Format(node.Amount)} / {NumberFormatting.Format(node.Capacity)}"
                        : $"{node.Resource} · {NumberFormatting.Format(node.Amount)}"
                };

                var border = new Border
                {
                    Tag = node,
                    Width = nodeWidth,
                    Height = nodeHeight,
                    Background = ThemeBrush("PanelBackgroundBrush", Brushes.White),
                    BorderBrush = (ReferenceEquals(node, _selectedEconomyNode) || _economySelectedNodeIds.Contains(node.Id)) ? kindBrush : new SolidColorBrush(Color.FromRgb(205, 210, 218)),
                    BorderThickness = new Thickness((ReferenceEquals(node, _selectedEconomyNode) || _economySelectedNodeIds.Contains(node.Id)) ? 3 : 1.5),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(7, 5, 7, 6),
                    Cursor = EconomyConnectToggle.IsChecked == true ? Cursors.Cross : Cursors.SizeAll,
                    ToolTip = string.IsNullOrWhiteSpace(node.Notes) ? $"{node.Kind} · {node.Resource}" : node.Notes
                };

                var stack = new StackPanel();
                stack.Children.Add(new Border { Height = 4, Background = resourceBrush, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 5) });
                stack.Children.Add(new TextBlock { Text = node.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                stack.Children.Add(new TextBlock { Text = $"{EconomyVisuals.KindGlyph(node.Kind)}  {node.Kind}", FontSize = 10, Foreground = kindBrush, Margin = new Thickness(0, 1, 0, 0) });
                stack.Children.Add(new TextBlock { Text = subtitle, FontSize = 9.3, Foreground = Brushes.DimGray, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                border.Child = stack;
                border.MouseLeftButtonDown += EconomyNodeMouseDown;
                border.MouseMove += EconomyNodeMouseMove;
                border.MouseLeftButtonUp += EconomyNodeMouseUp;
                Canvas.SetLeft(border, node.X);
                Canvas.SetTop(border, node.Y);
                EconomyCanvas.Children.Add(border);
                AddEconomyPorts(node, resourceBrush, nodeWidth, nodeHeight);
            }
        }

        private void RenderEconomyResourceLegend()
        {
            if (EconomyResourceLegendPanel == null || EconomyResourceLegendBorder == null) return;
            EconomyResourceLegendPanel.Children.Clear();
            EnsureEconomyResourceStyles();
            List<string> resources = EconomyNodes
                .Select(n => n.Resource)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();
            EconomyResourceLegendBorder.Visibility = resources.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (string resource in resources)
            {
                EconomyResourceStyle? style = EconomyResourceStyles.FirstOrDefault(s => s.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase));
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Fill = EconomyResourceBrush(resource),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                if (!string.IsNullOrWhiteSpace(style?.Icon))
                    row.Children.Add(new TextBlock { Text = style.Icon + " ", FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center });
                string unit = string.IsNullOrWhiteSpace(style?.Unit) ? string.Empty : $" ({style.Unit})";
                row.Children.Add(new TextBlock { Text = resource + unit, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center });
                EconomyResourceLegendPanel.Children.Add(row);
            }
        }

        private static (double sx, double sy, double ex, double ey) EconomyArrowEndpoints(
            double x1, double y1, double x2, double y2)
        {
            const double halfW = 68;
            const double halfH = 40;
            double dx = x2 - x1, dy = y2 - y1;
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return (x1, y1, x2, y2);

            double sourceScale = 1.0 / Math.Max(Math.Abs(dx) / halfW, Math.Abs(dy) / halfH);
            double sx = x1 + dx * sourceScale;
            double sy = y1 + dy * sourceScale;

            double targetScale = 1.0 / Math.Max(Math.Abs(dx) / halfW, Math.Abs(dy) / halfH);
            double ex = x2 - dx * targetScale;
            double ey = y2 - dy * targetScale;

            double len = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / len, uy = dy / len;
            return (sx + ux * 3, sy + uy * 3, ex - ux * 3, ey - uy * 3);
        }

        private void RegisterEconomyVisualTransfer(EconomyLink link, double amount)
        {
            if (amount <= 1e-10) return;
            double now = _economyClock.Elapsed.TotalSeconds;
            _economyVisualPendingAmount[link.Id] = _economyVisualPendingAmount.GetValueOrDefault(link.Id) + amount;

            // Continuous flows can fire every fixed step; batch those into a readable packet train.
            double spacing = link.Interval > 1e-9 ? 0.035 : 0.11;
            if (_economyVisualLastSpawn.TryGetValue(link.Id, out double last) && now - last < spacing) return;

            double pending = _economyVisualPendingAmount.GetValueOrDefault(link.Id);
            _economyVisualPendingAmount[link.Id] = 0;
            _economyVisualLastSpawn[link.Id] = now;

            bool followsSimulationTime = link.Delay > 1e-9;
            double duration = followsSimulationTime ? Math.Max(0.05, link.Delay) : 0.9;
            int packets = Math.Clamp(1 + (int)Math.Floor(Math.Log10(1 + pending)), 1, 5);
            _economyVisualPulses.Add(new EconomyFlowVisualPulse
            {
                LinkId = link.Id,
                StartedAt = followsSimulationTime ? _economyTime : now,
                Duration = duration,
                UsesSimulationTime = followsSimulationTime,
                Amount = pending,
                PacketCount = packets
            });

            if (_economyVisualPulses.Count > 500)
                _economyVisualPulses.RemoveAll(p => EconomyPulseProgress(p, now) > 1.5);
        }

        private double EconomyVisualTraffic(Guid linkId)
        {
            double now = _economyClock.Elapsed.TotalSeconds;
            return _economyVisualPulses
                .Where(p => p.LinkId == linkId && EconomyPulseProgress(p, now) <= 1)
                .Sum(p => p.Amount / Math.Max(0.1, p.Duration));
        }

        private void AddEconomyResourceArrow(
            double x1, double y1, double x2, double y2,
            Brush sourceBrush, Brush targetBrush, double thickness, double opacity, bool selected, bool delayed, bool conversion)
        {
            (double sx, double sy, double ex, double ey) = EconomyArrowEndpoints(x1, y1, x2, y2);
            double dx = ex - sx, dy = ey - sy;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;

            if (selected)
            {
                EconomyCanvas.Children.Add(new Line
                {
                    X1 = sx, Y1 = sy, X2 = ex, Y2 = ey,
                    Stroke = Brushes.RoyalBlue,
                    StrokeThickness = thickness + 6,
                    Opacity = 0.17,
                    IsHitTestVisible = false
                });
            }

            DoubleCollection? dash = delayed ? new DoubleCollection { 4, 2 } : null;
            double mx = (sx + ex) * 0.5, my = (sy + ey) * 0.5;
            if (conversion)
            {
                EconomyCanvas.Children.Add(new Line { X1 = sx, Y1 = sy, X2 = mx, Y2 = my, Stroke = sourceBrush, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false, StrokeDashArray = dash });
                EconomyCanvas.Children.Add(new Line { X1 = mx, Y1 = my, X2 = ex, Y2 = ey, Stroke = targetBrush, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false, StrokeDashArray = dash });

                var conversionMark = new Rectangle
                {
                    Width = 9,
                    Height = 9,
                    Fill = targetBrush,
                    Stroke = sourceBrush,
                    StrokeThickness = 2,
                    RenderTransform = new RotateTransform(45, 4.5, 4.5),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(conversionMark, mx - 4.5);
                Canvas.SetTop(conversionMark, my - 4.5);
                EconomyCanvas.Children.Add(conversionMark);
            }
            else
            {
                EconomyCanvas.Children.Add(new Line { X1 = sx, Y1 = sy, X2 = ex, Y2 = ey, Stroke = sourceBrush, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false, StrokeDashArray = dash });
            }

            double angle = Math.Atan2(dy, dx);
            const double head = 10;
            Point tip = new(ex, ey);
            Point left = new(ex - head * Math.Cos(angle - 0.52), ey - head * Math.Sin(angle - 0.52));
            Point right = new(ex - head * Math.Cos(angle + 0.52), ey - head * Math.Sin(angle + 0.52));
            EconomyCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection { tip, left, right },
                Fill = targetBrush,
                Stroke = Brushes.White,
                StrokeThickness = 0.8,
                Opacity = opacity,
                IsHitTestVisible = false
            });

            // A second, smaller chevron makes direction readable even when the endpoint sits behind a card.
            double cp = 0.72;
            double cx = sx + dx * cp, cy = sy + dy * cp;
            const double chevron = 6;
            EconomyCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection
                {
                    new(cx - chevron * Math.Cos(angle - 0.65), cy - chevron * Math.Sin(angle - 0.65)),
                    new(cx, cy),
                    new(cx - chevron * Math.Cos(angle + 0.65), cy - chevron * Math.Sin(angle + 0.65))
                },
                Stroke = conversion && cp < 0.5 ? sourceBrush : targetBrush,
                StrokeThickness = Math.Max(1.4, thickness * 0.8),
                Opacity = Math.Min(1, opacity + 0.05),
                IsHitTestVisible = false
            });
        }

        private void AddEconomyFlowHitTarget(EconomyLink link, double x1, double y1, double x2, double y2, double thickness)
        {
            (double sx, double sy, double ex, double ey) = EconomyArrowEndpoints(x1, y1, x2, y2);
            EconomyNode? source = EconomyNodes.FirstOrDefault(n => n.Id == link.SourceId);
            EconomyNode? target = EconomyNodes.FirstOrDefault(n => n.Id == link.TargetId);
            string route = source != null && target != null ? $"{source.Name} → {target.Name}" : "Flow";
            string flowName = string.IsNullOrWhiteSpace(link.Label) ? route : $"{link.Label} · {route}";
            var hit = new Line
            {
                X1 = sx, Y1 = sy, X2 = ex, Y2 = ey,
                Stroke = Brushes.Transparent,
                StrokeThickness = Math.Max(14, thickness + 9),
                Cursor = Cursors.Hand,
                Tag = link,
                ToolTip = $"{flowName}\n{link.Status}"
            };
            hit.MouseLeftButtonDown += EconomyFlowMouseDown;
            EconomyCanvas.Children.Add(hit);
        }

        private void EconomyFlowMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Shape { Tag: EconomyLink link }) return;
            e.Handled = true;
            _selectedEconomyNode = null;
            _selectedEconomyLink = link;
            EconomyLinksListBox.SelectedItem = link;
            UpdateEconomyInspector();
            RenderEconomyDiagram();
        }

        private void AddEconomyFlowPackets(
            EconomyLink link, double x1, double y1, double x2, double y2,
            Brush sourceBrush, Brush targetBrush)
        {
            (double sx, double sy, double ex, double ey) = EconomyArrowEndpoints(x1, y1, x2, y2);
            double dx = ex - sx, dy = ey - sy;
            if (dx * dx + dy * dy < 4) return;

            double now = _economyClock.Elapsed.TotalSeconds;
            _economyVisualPulses.RemoveAll(p => EconomyPulseProgress(p, now) > 1.25);
            foreach (EconomyFlowVisualPulse pulse in _economyVisualPulses.Where(p => p.LinkId == link.Id))
            {
                double baseProgress = EconomyPulseProgress(pulse, now);
                for (int i = 0; i < pulse.PacketCount; i++)
                {
                    double p = baseProgress - i * 0.09;
                    if (p < 0 || p > 1) continue;

                    double x = sx + dx * p;
                    double y = sy + dy * p;
                    Brush fill = p < 0.5 ? sourceBrush : targetBrush;
                    double efficiencyScale = p <= 0.5 ? 1 : Math.Sqrt(Math.Clamp(link.Efficiency, 0.08, 1.5));
                    double size = (6.5 + Math.Min(3.5, Math.Log10(1 + pulse.Amount))) * efficiencyScale;
                    var packet = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = fill,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.2,
                        Opacity = 0.97,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(packet, x - size / 2);
                    Canvas.SetTop(packet, y - size / 2);
                    EconomyCanvas.Children.Add(packet);
                }
            }
        }

        private void SyncEconomyHistorySeriesSelector()
        {
            if (EconomyHistorySeriesPanel == null) return;
            _updatingEconomyHistorySelector = true;
            try
            {
                HashSet<Guid> valid = EconomyNodes.Select(n => n.Id).ToHashSet();
                _economyHistoryVisibleSeries.RemoveWhere(id => !valid.Contains(id));
                if (!_economyHistorySelectionTouched)
                {
                    _economyHistoryVisibleSeries.Clear();
                    foreach (EconomyNode node in EconomyNodes.Where(n => n.IsStorage))
                        _economyHistoryVisibleSeries.Add(node.Id);
                }

                EconomyHistorySeriesPanel.Children.Clear();
                foreach (EconomyNode node in EconomyNodes)
                {
                    var check = new CheckBox
                    {
                        Content = $"{node.Name} [{node.Resource}]",
                        Tag = node.Id,
                        IsChecked = _economyHistoryVisibleSeries.Contains(node.Id),
                        Foreground = EconomyResourceBrush(node.Resource),
                        FontSize = 10.5,
                        Margin = new Thickness(0, 0, 12, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    check.Checked += EconomyHistorySeriesCheck_Changed;
                    check.Unchecked += EconomyHistorySeriesCheck_Changed;
                    EconomyHistorySeriesPanel.Children.Add(check);
                }
            }
            finally
            {
                _updatingEconomyHistorySelector = false;
            }
        }

        private void EconomyHistorySeriesCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingEconomyHistorySelector || sender is not CheckBox { Tag: Guid id } check) return;
            _economyHistorySelectionTouched = true;
            if (check.IsChecked == true) _economyHistoryVisibleSeries.Add(id);
            else _economyHistoryVisibleSeries.Remove(id);
            RenderEconomyChart();
        }

        private void EconomyHistorySeriesAllButton_Click(object sender, RoutedEventArgs e)
        {
            _economyHistorySelectionTouched = true;
            _economyHistoryVisibleSeries.Clear();
            foreach (EconomyNode node in EconomyNodes) _economyHistoryVisibleSeries.Add(node.Id);
            SyncEconomyHistorySeriesSelector();
            RenderEconomyChart();
        }

        private void EconomyHistorySeriesNoneButton_Click(object sender, RoutedEventArgs e)
        {
            _economyHistorySelectionTouched = true;
            _economyHistoryVisibleSeries.Clear();
            SyncEconomyHistorySeriesSelector();
            RenderEconomyChart();
        }

        private void EconomyHistoryDisplayChanged(object sender, RoutedEventArgs e) => RenderEconomyChart();
        private void EconomyHistoryDisplayTextChanged(object sender, TextChangedEventArgs e) => RenderEconomyChart();

        private void RenderEconomyChart()
        {
            if (!IsLoaded || EconomyChartCanvas == null || _workspaceMode != WorkspaceMode.EconomyDesigner) return;
            EconomyChartCanvas.Children.Clear();
            double width = EconomyChartCanvas.ActualWidth, height = EconomyChartCanvas.ActualHeight;
            if (width < 120 || height < 80 || _economyHistory.Count == 0) return;

            List<EconomyNode> series = EconomyNodes.Where(n => _economyHistoryVisibleSeries.Contains(n.Id)).ToList();
            if (series.Count == 0)
            {
                var empty = new TextBlock { Text = "Select one or more history series above.", Foreground = Brushes.Gray, FontSize = 11 };
                Canvas.SetLeft(empty, 18); Canvas.SetTop(empty, 16); EconomyChartCanvas.Children.Add(empty);
                return;
            }

            double latestT = _economyHistory[^1].Time;
            double window = TryEconomyDouble(EconomyHistoryWindowTextBox?.Text, out double requestedWindow) && requestedWindow > 0
                ? requestedWindow
                : double.PositiveInfinity;
            double requestedMinT = double.IsFinite(window) ? latestT - window : double.NegativeInfinity;
            List<EconomyHistoryPoint> frames = _economyHistory.Where(h => h.Time >= requestedMinT - 1e-9).ToList();
            if (frames.Count == 0) frames.Add(_economyHistory[^1]);

            double minT = frames[0].Time;
            double maxT = Math.Max(minT + 1e-6, frames[^1].Time);
            double maxY;
            if (TryEconomyDouble(EconomyHistoryYMaxTextBox?.Text, out double fixedMax) && fixedMax > 0)
                maxY = fixedMax;
            else
            {
                maxY = 1;
                foreach (EconomyNode node in series)
                {
                    double observed = frames.Max(h => h.Amounts.TryGetValue(node.Id, out double v) ? v : 0);
                    maxY = Math.Max(maxY, observed);
                    if (EconomyHistoryCapacityCheckBox?.IsChecked == true && node.IsStorage && double.IsFinite(node.Capacity))
                        maxY = Math.Max(maxY, node.Capacity);
                }
                maxY *= 1.08;
            }

            const double left = 48, right = 10, top = 12, bottom = 24;
            double plotW = Math.Max(1, width - left - right);
            double plotH = Math.Max(1, height - top - bottom);

            for (int i = 0; i <= 4; i++)
            {
                double y = top + plotH * i / 4.0;
                EconomyChartCanvas.Children.Add(new Line { X1 = left, X2 = left + plotW, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(235, 237, 241)), StrokeThickness = 1 });
                double value = maxY * (1 - i / 4.0);
                var label = new TextBlock { Text = NumberFormatting.Format(value), FontSize = 9, Foreground = Brushes.Gray };
                Canvas.SetLeft(label, 3); Canvas.SetTop(label, y - 7); EconomyChartCanvas.Children.Add(label);
            }
            for (int i = 0; i <= 4; i++)
            {
                double x = left + plotW * i / 4.0;
                EconomyChartCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + plotH, Stroke = new SolidColorBrush(Color.FromRgb(244, 245, 247)), StrokeThickness = 1 });
                double time = minT + (maxT - minT) * i / 4.0;
                var label = new TextBlock { Text = $"{NumberFormatting.Format(time)}s", FontSize = 9, Foreground = Brushes.Gray };
                Canvas.SetLeft(label, Math.Clamp(x - 12, left, left + plotW - 25)); Canvas.SetTop(label, top + plotH + 4); EconomyChartCanvas.Children.Add(label);
            }

            for (int index = 0; index < series.Count; index++)
            {
                EconomyNode node = series[index];
                Brush color = EconomyResourceBrush(node.Resource);
                var line = new Polyline { Stroke = color, StrokeThickness = 2.2, Points = new PointCollection(), StrokeLineJoin = PenLineJoin.Round };
                if (series.Take(index).Any(n => n.Resource.Equals(node.Resource, StringComparison.OrdinalIgnoreCase)))
                    line.StrokeDashArray = new DoubleCollection { 5, 2 + (index % 3) * 2 };

                foreach (EconomyHistoryPoint frame in frames)
                {
                    if (!frame.Amounts.TryGetValue(node.Id, out double amount)) continue;
                    double x = left + (frame.Time - minT) / (maxT - minT) * plotW;
                    double y = top + plotH - Math.Clamp(amount / maxY, 0, 1) * plotH;
                    line.Points.Add(new Point(x, y));
                }
                EconomyChartCanvas.Children.Add(line);

                if (EconomyHistoryCapacityCheckBox?.IsChecked == true && node.IsStorage && double.IsFinite(node.Capacity) && node.Capacity <= maxY)
                {
                    double cy = top + plotH - node.Capacity / maxY * plotH;
                    EconomyChartCanvas.Children.Add(new Line
                    {
                        X1 = left, X2 = left + plotW, Y1 = cy, Y2 = cy,
                        Stroke = color, StrokeThickness = 1, Opacity = 0.28,
                        StrokeDashArray = new DoubleCollection { 3, 4 }
                    });
                }
            }

            var rangeLabel = new TextBlock
            {
                Text = $"{series.Count} series · t {NumberFormatting.Format(minT)}–{NumberFormatting.Format(maxT)} s · Y 0–{NumberFormatting.Format(maxY)}",
                FontSize = 9.5,
                Foreground = Brushes.DimGray,
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                Padding = new Thickness(3, 1, 3, 1)
            };
            Canvas.SetLeft(rangeLabel, left + 4); Canvas.SetTop(rangeLabel, top + 3); EconomyChartCanvas.Children.Add(rangeLabel);
        }
        private void UpdateEconomyDiagnostics()
        {
            if (EconomyDiagnosticsText == null) return;
            EconomyTimeText.Text = $"t = {_economyTime:0.00}";
            if (EconomyNodes.Count == 0)
            {
                EconomyDiagnosticsText.Text = "Add nodes or load an example to begin.";
                return;
            }

            List<EconomyNode> storage = EconomyNodes.Where(n => n.IsStorage).ToList();
            double stored = storage.Sum(n => n.Amount);
            double generated = EconomyNodes.Where(n => n.IsGenerator).Sum(n => n.Amount);
            double consumed = EconomyNodes.Where(n => n.Kind == EconomyNodeKind.Sink).Sum(n => n.Amount);
            var warnings = new List<string>();
            foreach (EconomyNode pool in storage)
            {
                double fill = pool.Capacity > 0 && double.IsFinite(pool.Capacity) ? pool.Amount / pool.Capacity : 0;
                if (_economyTime > 0.5 && pool.Amount <= 1e-6) warnings.Add($"{pool.Name} is starved");
                else if (fill > 0.97) warnings.Add($"{pool.Name} is capacity-bound");
            }

            if (_economyHistory.Count > 8)
            {
                EconomyHistoryPoint a = _economyHistory[Math.Max(0, _economyHistory.Count - 8)];
                EconomyHistoryPoint b = _economyHistory[^1];
                double oldStored = storage.Sum(n => a.Amounts.TryGetValue(n.Id, out double v) ? v : 0);
                double span = Math.Max(1e-6, b.Time - a.Time);
                double trend = (stored - oldStored) / span;
                if (Math.Abs(trend) > Math.Max(5, stored * 0.25)) warnings.Add(trend > 0 ? "stored value is growing quickly" : "stored value is draining quickly");
            }

            foreach (IGrouping<string, EconomyNode> group in storage.GroupBy(n => n.Resource, StringComparer.OrdinalIgnoreCase))
            {
                double value = group.Sum(n => n.Amount);
                double capacity = group.Where(n => double.IsFinite(n.Capacity)).Sum(n => n.Capacity);
                if (capacity > 0 && value > capacity * 0.95) warnings.Add($"{group.Key} is near aggregate capacity");
            }

            string resources = string.Join("   ", storage.GroupBy(n => n.Resource, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key} {NumberFormatting.Format(g.Sum(n => n.Amount))}"));
            List<string> audit = BuildEconomyModelAudit();
            string health = warnings.Count == 0 ? "No obvious runtime bottleneck in the current window." : string.Join(" · ", warnings.Distinct());
            string modelHealth = audit.Count == 0
                ? "Model audit: clean."
                : "Model audit: " + string.Join(" · ", audit.Take(4)) + (audit.Count > 4 ? $" · +{audit.Count - 4} more" : string.Empty);
            string pending = _economyPendingTransfers.Count > 0
                ? $"   In transit {_economyPendingTransfers.Count} ({NumberFormatting.Format(_economyPendingTransfers.Sum(t => t.Amount))})"
                : string.Empty;
            EconomyDiagnosticsText.Text = $"Stored {NumberFormatting.Format(stored)}   Generated {NumberFormatting.Format(generated)}   Sunk {NumberFormatting.Format(consumed)}{pending}\n{resources}\n{health}\n{modelHealth}";
        }
        private void LoadEconomyExampleButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmReplaceEconomy("Load this example and replace the current Economy Designer model?")) return;
            int index = EconomyExampleComboBox.SelectedIndex;
            ClearEconomyModel();
            if (index == 1) BuildInflationEconomyExample();
            else if (index == 2) BuildTwoCurrencyEconomyExample();
            else if (index == 3) BuildRandomLootEconomyExample();
            else if (index == 4) BuildSeasonalEconomyExample();
            else if (index == 5) BuildCraftingEconomyExample();
            else if (index == 6) BuildBranchingRouterEconomyExample();
            else BuildFaucetSinkEconomyExample();
            SyncEconomyParameters();
            ResetEconomySimulation();
        }
        private void BuildFaucetSinkEconomyExample()
        {
            EconomyProjectNameTextBox.Text = "Faucet / sink balance";
            EconomyNode source = NewEconomyNode(EconomyNodeKind.Source, "Quest rewards", "Gold", 45, 90, 0, 0);
            EconomyNode pool = NewEconomyNode(EconomyNodeKind.Pool, "Wallet", "Gold", 280, 90, 120, 600);
            EconomyNode sink = NewEconomyNode(EconomyNodeKind.Sink, "Upgrades", "Gold", 515, 90, 0, 0);
            AddEconomyLink(source, pool, "income", 1).Label = "Quest income";
            AddEconomyLink(pool, sink, "spend", 1).Label = "Upgrade spend";
        }
        private void BuildInflationEconomyExample()
        {
            EconomyProjectNameTextBox.Text = "Inflation pressure";
            EconomyNode source = NewEconomyNode(EconomyNodeKind.Source, "Faucets", "Gold", 35, 90, 0, 0);
            EconomyNode pool = NewEconomyNode(EconomyNodeKind.Pool, "Currency", "Gold", 270, 90, 200, 2000);
            EconomyNode sink = NewEconomyNode(EconomyNodeKind.Sink, "Sinks", "Gold", 510, 90, 0, 0);
            AddEconomyLink(source, pool, "baseincome*(1+growth*t)", 1).Label = "Growing faucet";
            AddEconomyLink(pool, sink, "basespend+0.02*source", 1).Label = "Dynamic sink";
        }
        private void BuildTwoCurrencyEconomyExample()
        {
            EconomyProjectNameTextBox.Text = "Two-currency live service";
            EconomyNode play = NewEconomyNode(EconomyNodeKind.Source, "Play", "Gold", 25, 65, 0, 0);
            EconomyNode gold = NewEconomyNode(EconomyNodeKind.Pool, "Gold wallet", "Gold", 230, 55, 150, 1200);
            EconomyNode premiumSource = NewEconomyNode(EconomyNodeKind.Source, "Premium faucet", "Gems", 25, 185, 0, 0);
            EconomyNode gems = NewEconomyNode(EconomyNodeKind.Pool, "Gem wallet", "Gems", 230, 180, 20, 200);
            EconomyNode upgrades = NewEconomyNode(EconomyNodeKind.Sink, "Upgrades", "Gold", 485, 55, 0, 0);
            EconomyNode cosmetics = NewEconomyNode(EconomyNodeKind.Sink, "Cosmetics", "Gems", 485, 180, 0, 0);
            AddEconomyLink(play, gold, "baseincome", 1).Label = "Gold income";
            AddEconomyLink(gold, upgrades, "basespend+0.01*source", 1).Label = "Upgrade spend";
            AddEconomyLink(premiumSource, gems, "premiumrate", 1).Label = "Gem income";
            AddEconomyLink(gems, cosmetics, "premiumspend", 1).Label = "Cosmetic spend";
        }

        private void BuildRandomLootEconomyExample()
        {
            EconomyProjectNameTextBox.Text = "Random loot loop";
            EconomyNode fights = NewEconomyNode(EconomyNodeKind.Source, "Enemy encounters", "Gold", 25, 80, 0, 0);
            EconomyNode wallet = NewEconomyNode(EconomyNodeKind.Pool, "Wallet", "Gold", 250, 80, 50, 1000);
            EconomyNode craft = NewEconomyNode(EconomyNodeKind.Sink, "Crafting", "Gold", 500, 80, 0, 0);
            EconomyLink drop = AddEconomyLink(fights, wallet, "reward*(1+0.5*rand)", 1);
            drop.Interval = 1;
            drop.Chance = 0.65;
            AddEconomyLink(wallet, craft, "craftspend", 1).Interval = 2;
        }

        private void BuildSeasonalEconomyExample()
        {
            EconomyProjectNameTextBox.Text = "Seasonal event economy";
            EconomyNode baseSource = NewEconomyNode(EconomyNodeKind.Source, "Daily play", "Tokens", 25, 55, 0, 0);
            EconomyNode eventSource = NewEconomyNode(EconomyNodeKind.Event, "Event bonus", "Tokens", 25, 175, 0, 0);
            EconomyNode tokens = NewEconomyNode(EconomyNodeKind.Pool, "Token wallet", "Tokens", 265, 105, 0, 1500);
            EconomyNode shop = NewEconomyNode(EconomyNodeKind.Sink, "Event shop", "Tokens", 520, 105, 0, 0);
            AddEconomyLink(baseSource, tokens, "dailyreward", 1).Interval = 1;
            EconomyLink eventFlow = AddEconomyLink(eventSource, tokens, "eventreward", 1);
            eventFlow.Interval = 1; eventFlow.StartTime = 7; eventFlow.EndTime = 14;
            AddEconomyLink(tokens, shop, "shopspend", 1).Interval = 1;
        }

        private void BuildCraftingEconomyExample()
        {
            EconomyProjectNameTextBox.Text = "Crafting conversion chain";
            EconomyProjectDescriptionTextBox.Text = "Ore is gathered, stored, converted to Iron, then spent on crafting. The flow changes colour where the resource changes.";
            EconomyNode mine = NewEconomyNode(EconomyNodeKind.Source, "Mine rewards", "Ore", 25, 70, 0, 0);
            EconomyNode ore = NewEconomyNode(EconomyNodeKind.Pool, "Ore stock", "Ore", 205, 70, 40, 400);
            EconomyNode smelter = NewEconomyNode(EconomyNodeKind.Converter, "Smelter", "Iron", 390, 70, 0, 250);
            EconomyNode iron = NewEconomyNode(EconomyNodeKind.Pool, "Iron stock", "Iron", 575, 70, 0, 350);
            EconomyNode forge = NewEconomyNode(EconomyNodeKind.Sink, "Forge", "Iron", 575, 205, 0, 0);
            AddEconomyLink(mine, ore, "orerate", 1).Label = "Gather ore";
            EconomyLink smelt = AddEconomyLink(ore, smelter, "smeltrate", 0.65);
            smelt.Label = "Smelt ore";
            smelt.Interval = 1;
            AddEconomyLink(smelter, iron, "min(source,transfercap)", 1).Label = "Move iron";
            EconomyLink craft = AddEconomyLink(iron, forge, "craftcost", 1);
            craft.Label = "Craft item";
            craft.Interval = 2;
        }

        private void BuildBranchingRouterEconomyExample()
        {
            EconomyProjectNameTextBox.Text = "Branching reward router";
            EconomyProjectDescriptionTextBox.Text = "A Gate works as a small routing buffer. Here it sends low balances to savings and redirects surplus into an automatic upgrade sink.";
            EconomyNode eventNode = NewEconomyNode(EconomyNodeKind.Event, "Mission complete", "Gold", 25, 95, 0, 0);
            EconomyNode gate = NewEconomyNode(EconomyNodeKind.Gate, "Reward router", "Gold", 225, 95, 0, 1000000);
            EconomyNode savings = NewEconomyNode(EconomyNodeKind.Pool, "Savings", "Gold", 455, 45, 80, 500);
            EconomyNode upgrade = NewEconomyNode(EconomyNodeKind.Sink, "Auto upgrade", "Gold", 455, 165, 0, 0);
            EconomyLink reward = AddEconomyLink(eventNode, gate, "reward", 1);
            reward.Label = "Mission reward";
            reward.Interval = 1;
            EconomyLink save = AddEconomyLink(gate, savings, "min(source,saveRate)", 1);
            save.Label = "Route to savings";
            save.ConditionExpression = "target<saveTarget";
            CompileEconomyLink(save);
            EconomyLink spend = AddEconomyLink(gate, upgrade, "min(source,spendRate)", 1);
            spend.Label = "Spend surplus";
            spend.ConditionExpression = "and(target>=0,source>surplusThreshold)";
            CompileEconomyLink(spend);
        }

        private EconomyNode NewEconomyNode(EconomyNodeKind kind, string name, string resource, double x, double y, double initial, double capacity)
        {
            var node = new EconomyNode
            {
                Kind = kind,
                Name = name,
                Resource = resource,
                X = x,
                Y = y,
                InitialAmount = initial,
                Amount = initial,
                Capacity = EconomyNode.StoresAmount(kind) ? Math.Max(0.0001, capacity) : double.PositiveInfinity
            };
            EconomyNodes.Add(node);
            return node;
        }
        private void ClearEconomyModel()
        {
            _economyPredictionCts?.Cancel();
            _economyOptimizationCts?.Cancel();
            EconomyPauseButton_Click(this, new RoutedEventArgs());
            EconomyLinks.Clear();
            EconomyNodes.Clear();
            EconomyParameters.Clear();
            EconomyScenarios.Clear();
            EconomyCohorts.Clear();
            EconomyTargets.Clear();
            EconomySubsystems.Clear();
            EconomyRecipes.Clear();
            EconomyResourceStyles.Clear();
            EconomyDebugEvents.Clear();
            _economySelectedNodeIds.Clear();
            _economyBreakpointLinkId = null;
            _economyReplayMode = false;
            if (EconomyReplayToggleButton != null) EconomyReplayToggleButton.Content = "Replay";
            if (EconomyReplaySlider != null) EconomyReplaySlider.IsEnabled = false;
            _economyHistory.Clear();
            _economyLastFlowRates.Clear();
            _economyVisualPulses.Clear();
            _economyVisualPendingAmount.Clear();
            _economyVisualLastSpawn.Clear();
            _economyHistoryVisibleSeries.Clear();
            _economyHistorySelectionTouched = false;
            _economyPendingTransfers.Clear();
            _economyNextActivation.Clear();
            _economyLinkRandoms.Clear();
            _lastOptimizationValues = null;
            if (EconomyApplyOptimizationButton != null) EconomyApplyOptimizationButton.IsEnabled = false;
            _selectedEconomyNode = null;
            _selectedEconomyLink = null;
            UpdateEconomyInspector();
        }
        private void EconomyExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            if (_economyHistory.Count == 0)
            {
                UpdateEconomyDiagnostics();
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export economy history",
                Filter = "CSV file (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = "economy-history.csv"
            };
            if (dialog.ShowDialog(this) != true) return;

            List<EconomyNode> pools = EconomyNodes.Where(n => n.IsStorage).ToList();
            var csv = new StringBuilder();
            csv.Append("time");
            foreach (EconomyNode pool in pools) csv.Append(',').Append(EscapeEconomyCsv(pool.Name));
            csv.AppendLine();
            foreach (EconomyHistoryPoint frame in _economyHistory)
            {
                csv.Append(frame.Time.ToString("G17", CultureInfo.InvariantCulture));
                foreach (EconomyNode pool in pools)
                {
                    double amount = frame.Amounts.TryGetValue(pool.Id, out double value) ? value : 0;
                    csv.Append(',').Append(amount.ToString("G17", CultureInfo.InvariantCulture));
                }
                csv.AppendLine();
            }
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(false));
            EconomyConnectHintText.Text = "Economy history exported.";
        }

        private static string EscapeEconomyCsv(string text) =>
            text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

        private static bool TryEconomyDouble(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
