using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _economyPredictionCts;
        private CancellationTokenSource? _economyOptimizationCts;

        private void EconomyCaptureScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            string name = string.IsNullOrWhiteSpace(EconomyScenarioNameTextBox.Text)
                ? $"Scenario {EconomyScenarios.Count + 1}"
                : EconomyScenarioNameTextBox.Text.Trim();
            EconomyScenario? scenario = EconomyScenarios.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (scenario == null)
            {
                scenario = new EconomyScenario { Name = name };
                EconomyScenarios.Add(scenario);
            }
            scenario.ParameterValues = CaptureEconomyParameterValues();
            EconomyScenarioComboBox.SelectedItem = scenario;
            EconomyConnectHintText.Text = $"Captured scenario '{scenario.Name}'.";
        }

        private void EconomyApplyScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyScenarioComboBox.SelectedItem is not EconomyScenario scenario) return;
            ApplyEconomyParameterValues(scenario.ParameterValues);
            EconomyConnectHintText.Text = $"Applied scenario '{scenario.Name}'.";
        }

        private void EconomyDeleteScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyScenarioComboBox.SelectedItem is EconomyScenario scenario) EconomyScenarios.Remove(scenario);
        }

        private void EconomyCaptureCohortButton_Click(object sender, RoutedEventArgs e)
        {
            string name = string.IsNullOrWhiteSpace(EconomyCohortNameTextBox.Text)
                ? $"Cohort {EconomyCohorts.Count + 1}"
                : EconomyCohortNameTextBox.Text.Trim();
            EconomyCohort? cohort = EconomyCohorts.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cohort == null)
            {
                cohort = new EconomyCohort { Name = name };
                EconomyCohorts.Add(cohort);
            }
            cohort.ParameterValues = CaptureEconomyParameterValues();
            if (TryEconomyDouble(EconomyCohortWeightTextBox.Text, out double weight)) cohort.Weight = Math.Max(0, weight);
            EconomyCohortComboBox.SelectedItem = cohort;
            EconomyConnectHintText.Text = $"Captured cohort '{cohort.Name}'.";
        }

        private void EconomyApplyCohortButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyCohortComboBox.SelectedItem is not EconomyCohort cohort) return;
            ApplyEconomyParameterValues(cohort.ParameterValues);
            EconomyCohortWeightTextBox.Text = cohort.Weight.ToString("G6", CultureInfo.InvariantCulture);
            EconomyConnectHintText.Text = $"Applied cohort '{cohort.Name}'.";
        }

        private void EconomyDeleteCohortButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyCohortComboBox.SelectedItem is EconomyCohort cohort) EconomyCohorts.Remove(cohort);
        }

        private Dictionary<string, double> CaptureEconomyParameterValues() =>
            EconomyParameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

        private void ApplyEconomyParameterValues(IReadOnlyDictionary<string, double> values)
        {
            foreach (EconomyParameter parameter in EconomyParameters)
                if (values.TryGetValue(parameter.Name, out double value)) parameter.Value = value;
            UpdateEconomyDiagnostics();
        }

        private void EconomySaveProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save economy project",
                Filter = "Graph Calculator economy (*.gceconomy)|*.gceconomy|JSON file (*.json)|*.json",
                DefaultExt = ".gceconomy",
                AddExtension = true,
                FileName = SafeEconomyFileName(EconomyProjectNameTextBox.Text) + ".gceconomy"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                EconomyProjectFile project = BuildEconomyProjectFile();
                string json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
                WriteTextAtomically(dialog.FileName, json);
                EconomyConnectHintText.Text = "Economy project saved. The file is self-contained and portable.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not save economy project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EconomyOpenProjectButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open economy project",
                Filter = "Graph Calculator economy (*.gceconomy;*.json)|*.gceconomy;*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (!ConfirmReplaceEconomy("Open this economy and replace the current Economy Designer model?")) return;

            try
            {
                EconomyProjectFile? project = JsonSerializer.Deserialize<EconomyProjectFile>(File.ReadAllText(dialog.FileName));
                if (project == null) throw new InvalidDataException("The economy project is empty or unreadable.");
                ValidateEconomyProjectForLoad(project);
                LoadEconomyProject(project);
                SetWorkspaceMode(WorkspaceMode.EconomyDesigner, updateCombo: true);
                EconomyConnectHintText.Text = $"Loaded '{project.Name}'.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open economy project", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private EconomyProjectFile BuildEconomyProjectFile()
        {
            return new EconomyProjectFile
            {
                Version = EconomyProjectFile.CurrentVersion,
                AppVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? string.Empty,
                Name = string.IsNullOrWhiteSpace(EconomyProjectNameTextBox.Text) ? "Economy model" : EconomyProjectNameTextBox.Text.Trim(),
                Description = EconomyProjectDescriptionTextBox.Text ?? string.Empty,
                SavedUtc = DateTime.UtcNow,
                SimulationTime = _economyTime,
                TimeStep = ReadEconomyDt(),
                PlaybackSpeed = TryEconomyDouble(EconomySpeedTextBox.Text, out double speed) ? speed : 1,
                Seed = ReadEconomySeed(),
                PredictionRuns = ParseEconomyInt(EconomyPredictionRunsTextBox.Text, 500, 1, 20000),
                PredictionHorizon = ReadEconomyPredictionHorizon(),
                SelectedScenario = (EconomyScenarioComboBox.SelectedItem as EconomyScenario)?.Name ?? string.Empty,
                SelectedCohort = (EconomyCohortComboBox.SelectedItem as EconomyCohort)?.Name ?? string.Empty,
                PopulationMix = EconomyPopulationMixCheckBox.IsChecked == true,
                HistoryWindowSeconds = TryEconomyDouble(EconomyHistoryWindowTextBox.Text, out double historyWindow) && historyWindow > 0 ? historyWindow : 0,
                HistoryYMaximum = TryEconomyDouble(EconomyHistoryYMaxTextBox.Text, out double historyYMax) && historyYMax > 0 ? historyYMax : 0,
                HistoryShowCapacities = EconomyHistoryCapacityCheckBox.IsChecked == true,
                HistorySelectionCustomized = _economyHistorySelectionTouched,
                HistoryVisibleNodeIds = _economyHistoryVisibleSeries.ToList(),
                Nodes = EconomyNodes.Select(ToWorkspaceEconomyNode).ToList(),
                Links = EconomyLinks.Select(ToWorkspaceEconomyLink).ToList(),
                Parameters = EconomyParameters.Select(p => new WorkspaceEconomyParameter { Name = p.Name, Minimum = p.Minimum, Maximum = p.Maximum, Value = p.Value }).ToList(),
                Scenarios = EconomyScenarios.Select(s => new WorkspaceEconomyScenario { Name = s.Name, ParameterValues = new Dictionary<string, double>(s.ParameterValues, StringComparer.OrdinalIgnoreCase) }).ToList(),
                Cohorts = EconomyCohorts.Select(c => new WorkspaceEconomyCohort { Name = c.Name, Weight = c.Weight, ParameterValues = new Dictionary<string, double>(c.ParameterValues, StringComparer.OrdinalIgnoreCase) }).ToList(),
                Targets = EconomyTargets.Select(t => new WorkspaceEconomyTarget { NodeId = t.NodeId, NodeName = t.NodeName, Minimum = t.Minimum, Maximum = t.Maximum, Weight = t.Weight }).ToList(),
                Subsystems = EconomySubsystems.Select(ToWorkspaceSubsystem).ToList(),
                Recipes = EconomyRecipes.Select(ToWorkspaceRecipe).ToList(),
                ResourceStyles = EconomyResourceStyles.Select(ToWorkspaceResourceStyle).ToList(),
                PendingTransfers = _economyPendingTransfers.Select(t => new WorkspaceEconomyPendingTransfer
                {
                    LinkId = t.LinkId,
                    TargetId = t.TargetId,
                    DueTime = t.DueTime,
                    Amount = t.Amount
                }).ToList(),
                NextActivations = new Dictionary<Guid, double>(_economyNextActivation),
                LinkRandomStates = _economyLinkRandoms.ToDictionary(
                    pair => pair.Key,
                    pair =>
                    {
                        EconomyRandomState state = pair.Value.CaptureState();
                        return new WorkspaceEconomyRandomState { State = state.State, HasSpareNormal = state.HasSpareNormal, SpareNormal = state.SpareNormal };
                    }),
                History = _economyHistory.TakeLast(600).Select(h => new WorkspaceEconomyHistoryPoint
                {
                    Time = h.Time,
                    Amounts = h.Amounts.ToDictionary(pair => pair.Key, pair => pair.Value)
                }).ToList()
            };
        }

        private void LoadEconomyProject(EconomyProjectFile project)
        {
            ClearEconomyModel();
            EconomyProjectNameTextBox.Text = string.IsNullOrWhiteSpace(project.Name) ? "Economy model" : project.Name;
            EconomyProjectDescriptionTextBox.Text = project.Description ?? string.Empty;
            EconomyDtTextBox.Text = project.TimeStep > 0 ? project.TimeStep.ToString("G6", CultureInfo.InvariantCulture) : "0.1";
            EconomySpeedTextBox.Text = project.PlaybackSpeed > 0 ? project.PlaybackSpeed.ToString("G6", CultureInfo.InvariantCulture) : "1";
            EconomySeedTextBox.Text = project.Seed.ToString(CultureInfo.InvariantCulture);
            EconomyPredictionRunsTextBox.Text = Math.Max(1, project.PredictionRuns).ToString(CultureInfo.InvariantCulture);
            EconomyPredictionHorizonTextBox.Text = (project.PredictionHorizon > 0 ? project.PredictionHorizon : 30).ToString("G6", CultureInfo.InvariantCulture);

            foreach (WorkspaceEconomyNode source in project.Nodes ?? [])
                EconomyNodes.Add(FromWorkspaceEconomyNode(source));
            foreach (WorkspaceEconomyLink source in project.Links ?? [])
                EconomyLinks.Add(FromWorkspaceEconomyLink(source));
            foreach (WorkspaceEconomySubsystem source in project.Subsystems ?? []) EconomySubsystems.Add(FromWorkspaceSubsystem(source));
            foreach (WorkspaceEconomyRecipe source in project.Recipes ?? []) EconomyRecipes.Add(FromWorkspaceRecipe(source));
            foreach (WorkspaceEconomyResourceStyle source in project.ResourceStyles ?? []) EconomyResourceStyles.Add(FromWorkspaceResourceStyle(source));

            SyncEconomyParameters();
            foreach (WorkspaceEconomyParameter source in project.Parameters ?? [])
            {
                EconomyParameter? parameter = EconomyParameters.FirstOrDefault(p => p.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                if (parameter == null) continue;
                parameter.Minimum = source.Minimum;
                parameter.Maximum = source.Maximum;
                parameter.Value = source.Value;
            }

            foreach (WorkspaceEconomyScenario source in project.Scenarios ?? [])
                EconomyScenarios.Add(new EconomyScenario { Name = source.Name, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
            foreach (WorkspaceEconomyCohort source in project.Cohorts ?? [])
                EconomyCohorts.Add(new EconomyCohort { Name = source.Name, Weight = source.Weight, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
            foreach (WorkspaceEconomyTarget source in project.Targets ?? [])
                EconomyTargets.Add(new EconomyTarget { NodeId = source.NodeId, NodeName = source.NodeName, Minimum = source.Minimum, Maximum = source.Maximum, Weight = source.Weight });
            EconomyScenario? selectedScenario = EconomyScenarios.FirstOrDefault(s => s.Name.Equals(project.SelectedScenario ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            EconomyCohort? selectedCohort = EconomyCohorts.FirstOrDefault(c => c.Name.Equals(project.SelectedCohort ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (selectedScenario != null) EconomyScenarioComboBox.SelectedItem = selectedScenario;
            else if (EconomyScenarios.Count > 0) EconomyScenarioComboBox.SelectedIndex = 0;
            if (selectedCohort != null) EconomyCohortComboBox.SelectedItem = selectedCohort;
            else if (EconomyCohorts.Count > 0) EconomyCohortComboBox.SelectedIndex = 0;
            EconomyPopulationMixCheckBox.IsChecked = project.PopulationMix;
            EconomyHistoryWindowTextBox.Text = project.HistoryWindowSeconds > 0 ? project.HistoryWindowSeconds.ToString("G6", CultureInfo.InvariantCulture) : "0";
            EconomyHistoryYMaxTextBox.Text = project.HistoryYMaximum > 0 ? project.HistoryYMaximum.ToString("G6", CultureInfo.InvariantCulture) : "auto";
            EconomyHistoryCapacityCheckBox.IsChecked = project.HistoryShowCapacities;
            _economyHistoryVisibleSeries.Clear();
            foreach (Guid id in project.HistoryVisibleNodeIds ?? [])
                if (EconomyNodes.Any(n => n.Id == id)) _economyHistoryVisibleSeries.Add(id);
            _economyHistorySelectionTouched = project.HistorySelectionCustomized;
            SyncEconomyHistorySeriesSelector();
            if (EconomyNodes.Count > 0) EconomyTargetNodeComboBox.SelectedIndex = 0;

            _economyTime = Math.Max(0, project.SimulationTime);
            _economyHistory.Clear();
            _economyLastFlowRates.Clear();
            _economyPendingTransfers.Clear();
            _economyNextActivation.Clear();
            ResetEconomyRandomStreams();

            if (project.Version >= 2)
            {
                foreach (WorkspaceEconomyPendingTransfer transfer in project.PendingTransfers ?? [])
                {
                    if (EconomyNodes.Any(n => n.Id == transfer.TargetId) && transfer.Amount > 0 && double.IsFinite(transfer.DueTime))
                        _economyPendingTransfers.Add(new EconomyPendingTransfer(transfer.LinkId, transfer.TargetId, transfer.DueTime, transfer.Amount));
                }
                foreach (var pair in project.NextActivations ?? new Dictionary<Guid, double>())
                    if (EconomyLinks.Any(l => l.Id == pair.Key) && double.IsFinite(pair.Value)) _economyNextActivation[pair.Key] = pair.Value;
                foreach (var pair in project.LinkRandomStates ?? new Dictionary<Guid, WorkspaceEconomyRandomState>())
                {
                    if (!EconomyLinks.Any(l => l.Id == pair.Key) || pair.Value == null) continue;
                    _economyLinkRandoms[pair.Key] = new EconomyDeterministicRandom(new EconomyRandomState(pair.Value.State, pair.Value.HasSpareNormal, pair.Value.SpareNormal));
                }
                foreach (WorkspaceEconomyHistoryPoint frame in project.History ?? [])
                {
                    if (!double.IsFinite(frame.Time)) continue;
                    _economyHistory.Add(new EconomyHistoryPoint(frame.Time, new Dictionary<Guid, double>(frame.Amounts ?? [])));
                }
            }

            foreach (EconomyLink link in EconomyLinks)
                if (!_economyNextActivation.ContainsKey(link.Id)) _economyNextActivation[link.Id] = Math.Max(link.StartTime, _economyTime);
            if (_economyHistory.Count == 0) RecordEconomyHistory();
            RenderEconomyDiagram();
            RenderEconomyChart();
            UpdateEconomyDiagnostics();
        }

        private static WorkspaceEconomyNode ToWorkspaceEconomyNode(EconomyNode node) => new()
        {
            Id = node.Id,
            Kind = node.Kind.ToString(),
            Name = node.Name,
            Resource = node.Resource,
            Notes = node.Notes,
            X = node.X,
            Y = node.Y,
            InitialAmount = node.InitialAmount,
            Amount = node.Amount,
            Capacity = node.IsStorage && double.IsFinite(node.Capacity) ? node.Capacity : 0,
            SubsystemId = node.SubsystemId,
            ActionQueued = node.ActionQueued
        };

        private static WorkspaceEconomyLink ToWorkspaceEconomyLink(EconomyLink link) => new()
        {
            Id = link.Id,
            SourceId = link.SourceId,
            TargetId = link.TargetId,
            RateExpression = link.RateExpression,
            ConditionExpression = link.ConditionExpression,
            Label = link.Label,
            Efficiency = link.Efficiency,
            Chance = link.Chance,
            Interval = link.Interval,
            Delay = link.Delay,
            StartTime = link.StartTime,
            EndTime = double.IsFinite(link.EndTime) ? link.EndTime : 0,
            IsEnabled = link.IsEnabled
        };

        private static EconomyNode FromWorkspaceEconomyNode(WorkspaceEconomyNode source)
        {
            EconomyNodeKind kind = Enum.TryParse(source.Kind, true, out EconomyNodeKind parsed) ? parsed : EconomyNodeKind.Pool;
            bool storage = EconomyNode.StoresAmount(kind);
            return new EconomyNode
            {
                Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
                Kind = kind,
                Name = string.IsNullOrWhiteSpace(source.Name) ? kind.ToString() : source.Name,
                Resource = source.Resource,
                Notes = source.Notes,
                X = source.X,
                Y = source.Y,
                InitialAmount = source.InitialAmount,
                Amount = source.Amount,
                Capacity = storage ? Math.Max(0.0001, source.Capacity) : double.PositiveInfinity,
                SubsystemId = source.SubsystemId,
                ActionQueued = source.ActionQueued
            };
        }

        private static EconomyLink FromWorkspaceEconomyLink(WorkspaceEconomyLink source) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            SourceId = source.SourceId,
            TargetId = source.TargetId,
            RateExpression = string.IsNullOrWhiteSpace(source.RateExpression) ? "0" : source.RateExpression,
            ConditionExpression = string.IsNullOrWhiteSpace(source.ConditionExpression) ? "1" : source.ConditionExpression,
            Label = source.Label ?? string.Empty,
            Efficiency = source.Efficiency,
            Chance = Math.Clamp(source.Chance, 0, 1),
            Interval = Math.Max(0, source.Interval),
            Delay = Math.Max(0, source.Delay),
            StartTime = Math.Max(0, source.StartTime),
            EndTime = source.EndTime <= 0 ? double.PositiveInfinity : source.EndTime,
            IsEnabled = source.IsEnabled
        };

        private bool ConfirmReplaceEconomy(string message)
        {
            if (EconomyNodes.Count == 0 && EconomyLinks.Count == 0) return true;
            return MessageBox.Show(this, message, "Replace economy model", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private static string SafeEconomyFileName(string? name)
        {
            string value = string.IsNullOrWhiteSpace(name) ? "economy-model" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            return string.IsNullOrWhiteSpace(value) ? "economy-model" : value;
        }

        private static void WriteTextAtomically(string path, string contents)
        {
            string directory = Path.GetDirectoryName(path) ?? ".";
            string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temp, contents, new UTF8Encoding(false));
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private async void EconomyRunPredictionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_economyPredictionCts != null) return;
            if (EconomyNodes.Count == 0) { EconomyPredictionResultsTextBox.Text = "Build or load an economy first."; return; }
            int invalidFlows = EconomyLinks.Count(l => l.IsEnabled && (l.CompiledRate == null || l.CompiledCondition == null));
            if (invalidFlows > 0)
            {
                EconomyPredictionResultsTextBox.Text = $"Prediction blocked: {invalidFlows} enabled flow(s) have invalid expressions. Fix or disable them first.";
                return;
            }
            int runs = ParseEconomyInt(EconomyPredictionRunsTextBox.Text, 500, 1, 20000);
            double horizon = ReadEconomyPredictionHorizon();
            double dt = ReadEconomyDt();
            long seed = ReadEconomySeed();
            EconomySimulationModel model = EconomySimulationModel.Capture(EconomyNodes, EconomyLinks, EconomyParameters, EconomyRecipes);
            Dictionary<string, double>? scenario = (EconomyScenarioComboBox.SelectedItem as EconomyScenario)?.ParameterValues is { } s
                ? new Dictionary<string, double>(s, StringComparer.OrdinalIgnoreCase) : null;
            Dictionary<string, double>? selectedCohort = (EconomyCohortComboBox.SelectedItem as EconomyCohort)?.ParameterValues is { } c
                ? new Dictionary<string, double>(c, StringComparer.OrdinalIgnoreCase) : null;
            int selectedCohortIndex = Math.Max(0, EconomyCohortComboBox.SelectedIndex);
            List<(string name, double weight, Dictionary<string, double> values)> cohorts = EconomyCohorts
                .Select(c => (c.Name, c.Weight, new Dictionary<string, double>(c.ParameterValues, StringComparer.OrdinalIgnoreCase))).ToList();
            bool populationMix = EconomyPopulationMixCheckBox.IsChecked == true && cohorts.Any(c => c.weight > 0);

            _economyPredictionCts = new CancellationTokenSource();
            CancellationToken token = _economyPredictionCts.Token;
            IProgress<int> progress = new Progress<int>(done =>
            {
                if (_economyPredictionCts != null)
                    EconomyPredictionResultsTextBox.Text = $"Running deterministic-seed simulations… {done:N0} / {runs:N0}";
            });
            EconomyRunPredictionButton.IsEnabled = false;
            EconomyCancelPredictionButton.IsEnabled = true;
            EconomyPredictionResultsTextBox.Text = $"Running {runs:N0} deterministic-seed simulations…";
            try
            {
                string report = await Task.Run(() =>
                {
                    var results = new List<EconomySimulationResult>(runs);
                    var selector = new EconomyDeterministicRandom(seed ^ 0x4D595DF4D0F33173L);
                    for (int i = 0; i < runs; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var overrides = scenario == null
                            ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, double>(scenario, StringComparer.OrdinalIgnoreCase);
                        int cohortIndex = 0;
                        if (populationMix)
                        {
                            cohortIndex = PickWeightedCohort(cohorts, selector);
                            if (cohortIndex >= 0)
                                foreach (var pair in cohorts[cohortIndex].values) overrides[pair.Key] = pair.Value;
                        }
                        else if (selectedCohort != null)
                        {
                            foreach (var pair in selectedCohort) overrides[pair.Key] = pair.Value;
                            cohortIndex = selectedCohortIndex;
                        }
                        results.Add(EconomySimulationEngine.Run(model, horizon, dt, unchecked(seed + i * 7919L), overrides, i, cohortIndex));
                        if ((i + 1) % Math.Max(1, runs / 100) == 0 || i + 1 == runs) progress.Report(i + 1);
                    }
                    return BuildPredictionReport(model, results, horizon, runs, populationMix ? cohorts : null);
                }, token);
                EconomyPredictionResultsTextBox.Text = report;
            }
            catch (OperationCanceledException)
            {
                EconomyPredictionResultsTextBox.Text = "Prediction cancelled.";
            }
            catch (Exception ex)
            {
                EconomyPredictionResultsTextBox.Text = "Prediction failed: " + ex.Message;
            }
            finally
            {
                _economyPredictionCts?.Dispose();
                _economyPredictionCts = null;
                EconomyRunPredictionButton.IsEnabled = true;
                EconomyCancelPredictionButton.IsEnabled = false;
            }
        }

        private void EconomyCancelPredictionButton_Click(object sender, RoutedEventArgs e) => _economyPredictionCts?.Cancel();

        private static int PickWeightedCohort(IReadOnlyList<(string name, double weight, Dictionary<string, double> values)> cohorts, EconomyDeterministicRandom random)
        {
            double total = cohorts.Sum(c => Math.Max(0, c.weight));
            if (total <= 0) return -1;
            double roll = random.NextDouble() * total;
            for (int i = 0; i < cohorts.Count; i++)
            {
                roll -= Math.Max(0, cohorts[i].weight);
                if (roll <= 0) return i;
            }
            return cohorts.Count - 1;
        }

        private static string BuildPredictionReport(
            EconomySimulationModel model,
            IReadOnlyList<EconomySimulationResult> results,
            double horizon,
            int runs,
            IReadOnlyList<(string name, double weight, Dictionary<string, double> values)>? cohorts)
        {
            var text = new StringBuilder();
            text.AppendLine($"{runs:N0} runs to t={horizon:G6}");
            if (cohorts != null)
                text.AppendLine("Population mix: " + string.Join(", ", cohorts.Where(c => c.weight > 0).Select(c => $"{c.name} {c.weight:G4}")));

            int truncated = results.Count(r => r.StepLimitHit);
            if (truncated > 0)
                text.AppendLine($"WARNING: {truncated:N0} run(s) hit the simulation step limit before the requested horizon.");
            double averagePending = results.Count == 0 ? 0 : results.Average(r => r.PendingAmount);
            if (averagePending > 1e-9)
                text.AppendLine($"Average value still in transit at horizon: {averagePending:G6}");
            text.AppendLine();

            foreach (EconomyNodeSpec node in model.Nodes.Where(n => EconomySimulationEngine.IsStorage(n.Kind) || n.Kind == EconomyNodeKind.Sink))
            {
                double[] values = results.Select(r => r.FinalAmounts.TryGetValue(node.Id, out double value) ? value : 0).OrderBy(v => v).ToArray();
                if (values.Length == 0) continue;
                double mean = values.Average();
                double variance = values.Length > 1 ? values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1) : 0;
                double sigma = Math.Sqrt(Math.Max(0, variance));
                double median = Percentile(values, 0.5);
                double p10 = Percentile(values, 0.1);
                double p90 = Percentile(values, 0.9);
                double starved = results.Count(r => r.StarvedNodes.Contains(node.Id)) * 100.0 / results.Count;
                double capped = results.Count(r => r.CapacityBoundNodes.Contains(node.Id)) * 100.0 / results.Count;
                text.AppendLine($"{node.Name} [{node.Resource}]");
                text.AppendLine($"  mean {mean:G6}   σ {sigma:G6}   median {median:G6}");
                text.AppendLine($"  P10 {p10:G6}   P90 {p90:G6}   min {values[0]:G6}   max {values[^1]:G6}");
                text.AppendLine($"  starved {starved:0.0}%   capacity-bound {capped:0.0}%");
            }
            return text.ToString().TrimEnd();
        }

        private static readonly int[] EconomyHaltonBases =
        [
            2, 3, 5, 7, 11, 13, 17, 19,
            23, 29, 31, 37, 41, 43, 47, 53,
            59, 61, 67, 71, 73, 79, 83, 89,
            97, 101, 103, 107, 109, 113, 127, 131
        ];

        private static double Halton(int index, int dimension)
        {
            int basis = EconomyHaltonBases[Math.Abs(dimension) % EconomyHaltonBases.Length];
            double result = 0;
            double fraction = 1.0 / basis;
            int value = Math.Max(1, index);
            while (value > 0)
            {
                result += fraction * (value % basis);
                value /= basis;
                fraction /= basis;
            }
            return result;
        }

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            if (sorted.Length == 1) return sorted[0];
            double index = Math.Clamp(p, 0, 1) * (sorted.Length - 1);
            int lo = (int)Math.Floor(index), hi = (int)Math.Ceiling(index);
            if (lo == hi) return sorted[lo];
            double t = index - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
        }

        private void EconomyAddTargetButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyTargetNodeComboBox.SelectedItem is not EconomyNode node) return;
            if (!TryEconomyDouble(EconomyTargetMinTextBox.Text, out double min) || !TryEconomyDouble(EconomyTargetMaxTextBox.Text, out double max)) return;
            if (max < min) (min, max) = (max, min);
            EconomyTarget? existing = EconomyTargets.FirstOrDefault(t => t.NodeId == node.Id);
            if (existing != null) EconomyTargets.Remove(existing);
            EconomyTargets.Add(new EconomyTarget { NodeId = node.Id, NodeName = node.Name, Minimum = min, Maximum = max, Weight = 1 });
        }

        private void EconomyDeleteTargetButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyTargetsListBox.SelectedItem is EconomyTarget target) EconomyTargets.Remove(target);
        }

        private async void EconomyOptimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_economyOptimizationCts != null) return;
            if (EconomyTargets.Count == 0) { EconomyOptimizationResultsTextBox.Text = "Add at least one target first."; return; }
            int invalidFlows = EconomyLinks.Count(l => l.IsEnabled && (l.CompiledRate == null || l.CompiledCondition == null));
            if (invalidFlows > 0)
            {
                EconomyOptimizationResultsTextBox.Text = $"Balance search blocked: {invalidFlows} enabled flow(s) have invalid expressions.";
                return;
            }
            if (EconomyParameters.Count == 0) { EconomyOptimizationResultsTextBox.Text = "This model has no adjustable parameters."; return; }

            int candidates = ParseEconomyInt(EconomyOptimizationCandidatesTextBox.Text, 300, 10, 5000);
            double horizon = ReadEconomyPredictionHorizon();
            double dt = ReadEconomyDt();
            long seed = ReadEconomySeed();
            EconomySimulationModel model = EconomySimulationModel.Capture(EconomyNodes, EconomyLinks, EconomyParameters, EconomyRecipes);
            var ranges = EconomyParameters.Select(p => (p.Name, p.Minimum, p.Maximum, p.Value)).ToArray();
            var targets = EconomyTargets.Select(t => new EconomyTarget { NodeId = t.NodeId, NodeName = t.NodeName, Minimum = t.Minimum, Maximum = t.Maximum, Weight = t.Weight }).ToArray();
            Dictionary<string, double>? scenario = (EconomyScenarioComboBox.SelectedItem as EconomyScenario)?.ParameterValues is { } scenarioValues
                ? new Dictionary<string, double>(scenarioValues, StringComparer.OrdinalIgnoreCase) : null;
            Dictionary<string, double>? cohort = (EconomyCohortComboBox.SelectedItem as EconomyCohort)?.ParameterValues is { } cohortValues
                ? new Dictionary<string, double>(cohortValues, StringComparer.OrdinalIgnoreCase) : null;

            var fixedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (scenario != null) fixedNames.UnionWith(scenario.Keys);
            if (cohort != null) fixedNames.UnionWith(cohort.Keys);
            if (ranges.All(r => fixedNames.Contains(r.Name)))
            {
                EconomyOptimizationResultsTextBox.Text = "The selected scenario/cohort fixes every parameter. Choose a less restrictive overlay or clear the selection before searching.";
                return;
            }

            _economyOptimizationCts = new CancellationTokenSource();
            CancellationToken token = _economyOptimizationCts.Token;
            IProgress<string> progress = new Progress<string>(message =>
            {
                if (_economyOptimizationCts != null) EconomyOptimizationResultsTextBox.Text = message;
            });
            EconomyOptimizationSearchButton.IsEnabled = false;
            EconomyCancelOptimizationButton.IsEnabled = true;
            EconomyOptimizationResultsTextBox.Text = $"Searching {candidates:N0} parameter sets…";
            EconomyApplyOptimizationButton.IsEnabled = false;
            try
            {
                var search = await Task.Run(() =>
                {
                    var rng = new EconomyDeterministicRandom(seed ^ 0x26A9F5D51B7B1A3DL);
                    var fixedOverrides = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    if (scenario != null) foreach (var pair in scenario) fixedOverrides[pair.Key] = pair.Value;
                    if (cohort != null) foreach (var pair in cohort) fixedOverrides[pair.Key] = pair.Value;
                    var adjustable = ranges.Where(r => !fixedOverrides.ContainsKey(r.Name)).ToArray();

                    (double score, double[] errors) EvaluateCandidate(Dictionary<string, double> values)
                    {
                        double score = 0;
                        double[] errors = new double[targets.Length];
                        const int scoreRuns = 5;
                        // Every candidate sees the same seeds. Otherwise random luck can masquerade as balance.
                        for (int r = 0; r < scoreRuns; r++)
                        {
                            token.ThrowIfCancellationRequested();
                            EconomySimulationResult result = EconomySimulationEngine.Run(model, horizon, dt, unchecked(seed + r * 104729L), values, r, 0);
                            double[] runErrors = EconomyTargetErrors(result, targets);
                            double runScore = 0;
                            for (int i = 0; i < runErrors.Length; i++)
                            {
                                errors[i] += runErrors[i] / scoreRuns;
                                runScore += runErrors[i] * runErrors[i] * Math.Max(0.0001, targets[i].Weight);
                            }
                            if (result.StepLimitHit) runScore += 1_000_000;
                            score += runScore / scoreRuns;
                        }
                        return (score, errors);
                    }

                    var explored = new List<(Dictionary<string, double> values, double score, double[] errors)>();
                    (double score, double[] errors) EvaluateTracked(Dictionary<string, double> values)
                    {
                        var evaluation = EvaluateCandidate(values);
                        explored.Add((new Dictionary<string, double>(values, StringComparer.OrdinalIgnoreCase), evaluation.score, evaluation.errors));
                        return evaluation;
                    }

                    Dictionary<string, double> MakeBase()
                    {
                        var values = new Dictionary<string, double>(fixedOverrides, StringComparer.OrdinalIgnoreCase);
                        foreach (var range in adjustable) values[range.Name] = range.Value;
                        return values;
                    }

                    Dictionary<string, double> bestValues = MakeBase();
                    var baseEvaluation = EvaluateTracked(bestValues);
                    double bestScore = baseEvaluation.score;

                    for (int candidate = 1; candidate < candidates && bestScore > 1e-12; candidate++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (candidate % Math.Max(1, candidates / 100) == 0)
                            progress.Report($"Sampling balance space… {candidate:N0} / {candidates:N0}");
                        var values = new Dictionary<string, double>(fixedOverrides, StringComparer.OrdinalIgnoreCase);
                        for (int dimension = 0; dimension < adjustable.Length; dimension++)
                        {
                            var range = adjustable[dimension];
                            double unit = Halton(candidate, dimension);
                            // A tiny deterministic jitter avoids repeatedly landing on exact rational boundaries.
                            unit = Math.Clamp(unit + (rng.NextDouble() - 0.5) / Math.Max(20, candidates), 0, 1);
                            values[range.Name] = range.Minimum + (range.Maximum - range.Minimum) * unit;
                        }

                        var evaluation = EvaluateTracked(values);
                        if (evaluation.score >= bestScore) continue;
                        bestScore = evaluation.score;
                        bestValues = values;
                    }

                    // Random search finds the basin; a few coordinate passes stop us from wasting that information.
                    progress.Report("Refining the best parameter basin…");
                    foreach (double fraction in new[] { 0.20, 0.08, 0.03 })
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var range in adjustable)
                        {
                            token.ThrowIfCancellationRequested();
                            double current = bestValues[range.Name];
                            double delta = (range.Maximum - range.Minimum) * fraction;
                            foreach (double direction in new[] { -1.0, 1.0 })
                            {
                                var trial = new Dictionary<string, double>(bestValues, StringComparer.OrdinalIgnoreCase)
                                {
                                    [range.Name] = Math.Clamp(current + delta * direction, range.Minimum, range.Maximum)
                                };
                                var evaluation = EvaluateTracked(trial);
                                if (evaluation.score >= bestScore) continue;
                                bestScore = evaluation.score;
                                bestValues = trial;
                                current = bestValues[range.Name];
                            }
                        }
                    }

                    bool Dominates((Dictionary<string, double> values, double score, double[] errors) a, (Dictionary<string, double> values, double score, double[] errors) b)
                    {
                        bool strictlyBetter = false;
                        for (int i = 0; i < Math.Min(a.errors.Length, b.errors.Length); i++)
                        {
                            if (a.errors[i] > b.errors[i] + 1e-10) return false;
                            if (a.errors[i] + 1e-10 < b.errors[i]) strictlyBetter = true;
                        }
                        return strictlyBetter;
                    }

                    var pareto = explored
                        .Where(candidate => !explored.Any(other => !ReferenceEquals(candidate.values, other.values) && Dominates(other, candidate)))
                        .OrderBy(candidate => candidate.score)
                        .Take(8)
                        .ToList();

                    return (bestValues: bestValues, bestScore: bestScore, pareto: pareto);
                }, token);

                _lastOptimizationValues = search.bestValues;
                EconomyApplyOptimizationButton.IsEnabled = search.bestValues.Count > 0;
                var report = new StringBuilder();
                report.AppendLine($"Best score: {search.bestScore:G6}  (0 means every target is inside range)");
                if (scenario != null || cohort != null) report.AppendLine("Selected scenario/cohort overrides were held fixed during the search.");
                foreach (var pair in search.bestValues.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                    if (ranges.Any(r => r.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))) report.AppendLine($"{pair.Key} = {pair.Value:G8}");

                if (search.pareto.Count > 1 && targets.Length > 1)
                {
                    report.AppendLine();
                    report.AppendLine("Pareto alternatives (different target trade-offs):");
                    int alternative = 1;
                    foreach (var candidate in search.pareto)
                    {
                        report.Append($"  {alternative++}. score {candidate.score:G5} · errors ");
                        report.AppendLine(string.Join(", ", candidate.errors.Select((error, i) => $"{targets[i].NodeName}:{error:G4}")));
                        report.AppendLine("     " + string.Join("  ", candidate.values.Where(p => ranges.Any(r => r.Name.Equals(p.Key, StringComparison.OrdinalIgnoreCase))).OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value:G5}")));
                    }
                }
                EconomyOptimizationResultsTextBox.Text = report.ToString().TrimEnd();
            }
            catch (OperationCanceledException)
            {
                EconomyOptimizationResultsTextBox.Text = "Balance search cancelled.";
            }
            catch (Exception ex)
            {
                EconomyOptimizationResultsTextBox.Text = "Search failed: " + ex.Message;
            }
            finally
            {
                _economyOptimizationCts?.Dispose();
                _economyOptimizationCts = null;
                EconomyOptimizationSearchButton.IsEnabled = true;
                EconomyCancelOptimizationButton.IsEnabled = false;
            }
        }

        private void EconomyCancelOptimizationButton_Click(object sender, RoutedEventArgs e) => _economyOptimizationCts?.Cancel();

        private static double[] EconomyTargetErrors(EconomySimulationResult result, IReadOnlyList<EconomyTarget> targets)
        {
            var errors = new double[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                EconomyTarget target = targets[i];
                double value = result.FinalAmounts.TryGetValue(target.NodeId, out double found) ? found : 0;
                if (value >= target.Minimum && value <= target.Maximum) { errors[i] = 0; continue; }
                double nearest = value < target.Minimum ? target.Minimum : target.Maximum;
                double scale = Math.Max(1, Math.Abs(target.Maximum - target.Minimum));
                errors[i] = Math.Abs(value - nearest) / scale;
            }
            return errors;
        }

        private static double ScoreEconomyTargets(EconomySimulationResult result, IReadOnlyList<EconomyTarget> targets)
        {
            double score = 0;
            foreach (EconomyTarget target in targets)
            {
                double value = result.FinalAmounts.TryGetValue(target.NodeId, out double found) ? found : 0;
                if (value >= target.Minimum && value <= target.Maximum) continue;
                double nearest = value < target.Minimum ? target.Minimum : target.Maximum;
                double scale = Math.Max(1, Math.Abs(target.Maximum - target.Minimum));
                double d = (value - nearest) / scale;
                score += d * d * Math.Max(0.0001, target.Weight);
            }
            return score;
        }

        private void EconomyApplyOptimizationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastOptimizationValues == null) return;
            ApplyEconomyParameterValues(_lastOptimizationValues);
            EconomyConnectHintText.Text = "Applied the best parameter set from the last balance search.";
        }

        private double ReadEconomyPredictionHorizon() =>
            TryEconomyDouble(EconomyPredictionHorizonTextBox.Text, out double value) ? Math.Clamp(value, 0.01, 1_000_000) : 30;

        private static int ParseEconomyInt(string? text, int fallback, int min, int max) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? Math.Clamp(value, min, max) : fallback;
    }
}
