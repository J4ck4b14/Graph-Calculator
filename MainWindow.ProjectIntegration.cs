using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        public ObservableCollection<SharedAsset> SharedAssets { get; } = new();
        public ObservableCollection<ImportedTable> ImportedTables { get; } = new();
        public ObservableCollection<CurveChannel> CurveChannels { get; } = new();
        public ObservableCollection<EconomySubsystem> EconomySubsystems { get; } = new();
        public ObservableCollection<EconomyRecipe> EconomyRecipes { get; } = new();
        public ObservableCollection<EconomyResourceStyle> EconomyResourceStyles { get; } = new();
        public ObservableCollection<EconomyDebugEvent> EconomyDebugEvents { get; } = new();

        private readonly HashSet<Guid> _economySelectedNodeIds = [];
        private CurveChannel? _activeCurveChannel;
        private double _curveAutoTension;
        private readonly DispatcherTimer _projectAutosaveTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
        private bool _projectDirty;
        private string? _currentWorkspacePath;
        private readonly List<string> _recentWorkspacePaths = [];
        private bool _economyReplayMode;
        private double _economyReplayTime;
        private Guid? _economyBreakpointLinkId;
        private readonly List<EconomyNode> _economyClipboardNodes = [];
        private readonly List<EconomyLink> _economyClipboardLinks = [];

        private string RecoveryDirectory => IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GraphCalculator");
        private string RecoveryFilePath => IOPath.Combine(RecoveryDirectory, "workspace-recovery.json");
        private string RecentFilePath => IOPath.Combine(RecoveryDirectory, "recent-workspaces.json");

        private void InitializeProjectIntegration()
        {
            _projectAutosaveTimer.Tick += (_, _) => WriteRecoveryFile();
            SharedAssetsListBox.ItemsSource = SharedAssets;
            ImportedTablesListBox.ItemsSource = ImportedTables;
            CurveChannelsComboBox.ItemsSource = CurveChannels;
            EconomySubsystemsListBox.ItemsSource = EconomySubsystems;
            EconomyRecipesListBox.ItemsSource = EconomyRecipes;
            EconomyResourceStylesListBox.ItemsSource = EconomyResourceStyles;
            EconomyDebuggerListBox.ItemsSource = EconomyDebugEvents;

            if (CurveChannels.Count == 0)
            {
                var primary = new CurveChannel { Name = "Value", ColorHex = "#4169E1" };
                CurveChannels.Add(primary);
                _activeCurveChannel = primary;
                CurveChannelsComboBox.SelectedItem = primary;
            }

            LoadRecentWorkspaceList();
            RecoveryButton.Visibility = File.Exists(RecoveryFilePath) ? Visibility.Visible : Visibility.Collapsed;
            PreviewKeyDown += IntegrationPreviewKeyDown;
            UpdateProjectDirtyUi();
        }

        private void IntegrationPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.S)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || string.IsNullOrWhiteSpace(_currentWorkspacePath))
                    SaveWorkspaceButton_Click(this, new RoutedEventArgs());
                else
                    SaveWorkspaceDirect(_currentWorkspacePath!);
                e.Handled = true;
            }
            else if (e.Key == Key.O)
            {
                LoadWorkspaceButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.G && _workspaceMode == WorkspaceMode.EconomyDesigner)
            {
                EconomyGroupSelectedButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.D && _workspaceMode == WorkspaceMode.EconomyDesigner)
            {
                EconomyDuplicateSelectedButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.C && _workspaceMode == WorkspaceMode.EconomyDesigner)
            {
                CopyEconomySelection();
                e.Handled = true;
            }
            else if (e.Key == Key.V && _workspaceMode == WorkspaceMode.EconomyDesigner)
            {
                PasteEconomySelection();
                e.Handled = true;
            }
        }

        private void MarkProjectDirty()
        {
            if (_loadingWorkspace) return;
            _projectDirty = true;
            UpdateProjectDirtyUi();
            _projectAutosaveTimer.Stop();
            _projectAutosaveTimer.Start();
        }

        private void UpdateProjectDirtyUi()
        {
            if (ProjectDirtyText == null) return;
            ProjectDirtyText.Text = _projectDirty ? "● Unsaved" : "Saved";
            ProjectDirtyText.Foreground = _projectDirty ? Brushes.DarkOrange : Brushes.SeaGreen;
        }

        private void WriteRecoveryFile()
        {
            _projectAutosaveTimer.Stop();
            if (!_projectDirty) return;
            try
            {
                Directory.CreateDirectory(RecoveryDirectory);
                IntegrationRecoveryState state = CaptureRecoveryState();
                File.WriteAllText(RecoveryFilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                RecoveryButton.Visibility = Visibility.Visible;
            }
            catch
            {
                // Recovery should never interrupt authoring. The regular Save command can still report errors.
            }
        }

        private IntegrationRecoveryState CaptureRecoveryState()
        {
            SaveActiveCurveChannel();
            return new IntegrationRecoveryState
            {
                TimestampUtc = DateTime.UtcNow,
                WorkspaceMode = _workspaceMode.ToString(),
                HlslScratchText = HlslPreviewTextBox?.Text ?? string.Empty,
                Expressions = Expressions.Select(e => new WorkspaceExpression
                {
                    Expression = e.Expression, IsVisible = e.IsVisible,
                    DomainMinX = e.DomainMinX, DomainMaxX = e.DomainMaxX,
                    DomainMinY = e.DomainMinY, DomainMaxY = e.DomainMaxY
                }).ToList(),
                Parameters = Parameters.Select(p => new WorkspaceParameter { Name = p.Name, Minimum = p.Minimum, Maximum = p.Maximum, Value = p.Value, IsAnimating = p.IsAnimating, AnimationSpeed = p.AnimationSpeed, AnimationMode = p.AnimationMode, DisplayName = p.DisplayName, Group = p.Group, Unit = p.Unit, IsLocked = p.IsLocked }).ToList(),
                CurveBeforeMode = _curveBeforeMode.ToString(),
                CurveAfterMode = _curveAfterMode.ToString(),
                CurveAutoTension = _curveAutoTension,
                CurveChannels = CurveChannels.Select(ToWorkspaceCurveChannel).ToList(),
                ActiveCurveChannelId = _activeCurveChannel?.Id ?? Guid.Empty,
                SharedAssets = SharedAssets.Select(ToWorkspaceSharedAsset).ToList(),
                ImportedTables = ImportedTables.Select(t => new WorkspaceImportedTable { Name = t.Name, XUnit = t.XUnit, YUnit = t.YUnit, Rows = t.Rows.ToList() }).ToList(),
                EconomyNodes = EconomyNodes.Select(ToWorkspaceEconomyNode).ToList(),
                EconomyLinks = EconomyLinks.Select(ToWorkspaceEconomyLink).ToList(),
                EconomyParameters = EconomyParameters.Select(p => new WorkspaceEconomyParameter { Name = p.Name, Minimum = p.Minimum, Maximum = p.Maximum, Value = p.Value }).ToList(),
                EconomyScenarios = EconomyScenarios.Select(x => new WorkspaceEconomyScenario { Name = x.Name, ParameterValues = new Dictionary<string, double>(x.ParameterValues, StringComparer.OrdinalIgnoreCase) }).ToList(),
                EconomyCohorts = EconomyCohorts.Select(x => new WorkspaceEconomyCohort { Name = x.Name, Weight = x.Weight, ParameterValues = new Dictionary<string, double>(x.ParameterValues, StringComparer.OrdinalIgnoreCase) }).ToList(),
                EconomyTargets = EconomyTargets.Select(x => new WorkspaceEconomyTarget { NodeId = x.NodeId, NodeName = x.NodeName, Minimum = x.Minimum, Maximum = x.Maximum, Weight = x.Weight }).ToList(),
                EconomySubsystems = EconomySubsystems.Select(ToWorkspaceSubsystem).ToList(),
                EconomyRecipes = EconomyRecipes.Select(ToWorkspaceRecipe).ToList(),
                EconomyResourceStyles = EconomyResourceStyles.Select(ToWorkspaceResourceStyle).ToList(),
                EconomyTime = _economyTime
            };
        }

        private void RecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(RecoveryFilePath)) return;
            if (MessageBox.Show(this, "Restore the most recent autosaved recovery state?", "Restore recovery", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                IntegrationRecoveryState? state = JsonSerializer.Deserialize<IntegrationRecoveryState>(File.ReadAllText(RecoveryFilePath));
                if (state == null) return;
                ApplyRecoveryState(state);
                GraphStatusText.Text = $"Recovered autosave from {state.TimestampUtc.ToLocalTime():g}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not restore recovery", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyRecoveryState(IntegrationRecoveryState state)
        {
            _loadingWorkspace = true;
            try
            {
                ClearWorkspaceCollections();
                CurveKeys.Clear();
                ClearEconomyModel();
                SharedAssets.Clear(); ImportedTables.Clear(); CurveChannels.Clear();
                EconomySubsystems.Clear(); EconomyRecipes.Clear(); EconomyResourceStyles.Clear();

                foreach (WorkspaceExpression source in state.Expressions)
                {
                    GraphExpression item = AddExpression(source.Expression);
                    item.IsVisible = source.IsVisible;
                    item.DomainMinX = source.DomainMinX; item.DomainMaxX = source.DomainMaxX;
                    item.DomainMinY = source.DomainMinY; item.DomainMaxY = source.DomainMaxY;
                }
                SyncParameters();
                foreach (WorkspaceParameter source in state.Parameters)
                {
                    GraphParameter? parameter = Parameters.FirstOrDefault(p => p.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    if (parameter == null) continue;
                    parameter.SetRangeAndValue(source.Minimum, source.Maximum, source.Value);
                    parameter.DisplayName = source.DisplayName; parameter.Group = source.Group; parameter.Unit = source.Unit;
                    parameter.AnimationSpeed = source.AnimationSpeed; parameter.AnimationMode = source.AnimationMode; parameter.IsAnimating = source.IsAnimating; parameter.IsLocked = source.IsLocked;
                }
                _curveBeforeMode = Enum.TryParse(state.CurveBeforeMode, true, out CurveExtrapolationMode recoveryBefore) ? recoveryBefore : CurveExtrapolationMode.Clamp;
                _curveAfterMode = Enum.TryParse(state.CurveAfterMode, true, out CurveExtrapolationMode recoveryAfter) ? recoveryAfter : CurveExtrapolationMode.Clamp;
                _curveAutoTension = Math.Clamp(state.CurveAutoTension, 0, 1); CurveDesignerMath.AutoTension = _curveAutoTension;
                SetCurveWrapCombo(CurveBeforeModeComboBox, _curveBeforeMode); SetCurveWrapCombo(CurveAfterModeComboBox, _curveAfterMode);
                if (CurveTensionSlider != null) CurveTensionSlider.Value = _curveAutoTension;
                foreach (WorkspaceSharedAsset item in state.SharedAssets) SharedAssets.Add(FromWorkspaceSharedAsset(item));
                foreach (WorkspaceImportedTable item in state.ImportedTables) ImportedTables.Add(new ImportedTable { Name = item.Name, XUnit = item.XUnit, YUnit = item.YUnit, Rows = item.Rows?.ToList() ?? [] });
                foreach (WorkspaceCurveChannel item in state.CurveChannels) CurveChannels.Add(FromWorkspaceCurveChannel(item));
                _activeCurveChannel = CurveChannels.FirstOrDefault(c => c.Id == state.ActiveCurveChannelId) ?? CurveChannels.FirstOrDefault();
                CurveChannelsComboBox.SelectedItem = _activeCurveChannel;
                LoadActiveCurveChannel();

                foreach (WorkspaceEconomyNode item in state.EconomyNodes) EconomyNodes.Add(FromWorkspaceEconomyNode(item));
                foreach (WorkspaceEconomyLink item in state.EconomyLinks) EconomyLinks.Add(FromWorkspaceEconomyLink(item));
                foreach (WorkspaceEconomySubsystem item in state.EconomySubsystems) EconomySubsystems.Add(FromWorkspaceSubsystem(item));
                foreach (WorkspaceEconomyRecipe item in state.EconomyRecipes) EconomyRecipes.Add(FromWorkspaceRecipe(item));
                foreach (WorkspaceEconomyResourceStyle item in state.EconomyResourceStyles) EconomyResourceStyles.Add(FromWorkspaceResourceStyle(item));
                _economyTime = Math.Max(0, state.EconomyTime);
                SyncEconomyParameters();
                foreach (WorkspaceEconomyParameter source in state.EconomyParameters)
                {
                    EconomyParameter? parameter = EconomyParameters.FirstOrDefault(p => p.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase));
                    if (parameter == null) continue;
                    parameter.Minimum = source.Minimum; parameter.Maximum = source.Maximum; parameter.Value = source.Value;
                }
                foreach (WorkspaceEconomyScenario source in state.EconomyScenarios) EconomyScenarios.Add(new EconomyScenario { Name = source.Name, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
                foreach (WorkspaceEconomyCohort source in state.EconomyCohorts) EconomyCohorts.Add(new EconomyCohort { Name = source.Name, Weight = source.Weight, ParameterValues = new Dictionary<string, double>(source.ParameterValues ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase) });
                foreach (WorkspaceEconomyTarget source in state.EconomyTargets) EconomyTargets.Add(new EconomyTarget { NodeId = source.NodeId, NodeName = source.NodeName, Minimum = source.Minimum, Maximum = source.Maximum, Weight = source.Weight });
                if (HlslPreviewTextBox != null) HlslPreviewTextBox.Text = state.HlslScratchText ?? string.Empty;
            }
            finally { _loadingWorkspace = false; }
            SetWorkspaceMode(Enum.TryParse(state.WorkspaceMode, out WorkspaceMode mode) ? mode : WorkspaceMode.FunctionLab, true);
            _projectDirty = true;
            UpdateProjectDirtyUi();
            ScheduleRender(0);
        }

        private void RememberRecentWorkspace(string path)
        {
            path = IOPath.GetFullPath(path);
            _recentWorkspacePaths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            _recentWorkspacePaths.Insert(0, path);
            while (_recentWorkspacePaths.Count > 10) _recentWorkspacePaths.RemoveAt(_recentWorkspacePaths.Count - 1);
            RecentWorkspaceComboBox.ItemsSource = null;
            RecentWorkspaceComboBox.ItemsSource = _recentWorkspacePaths.Select(IOPath.GetFileName).ToList();
            RecentWorkspaceComboBox.SelectedIndex = _recentWorkspacePaths.Count > 0 ? 0 : -1;
            try
            {
                Directory.CreateDirectory(RecoveryDirectory);
                File.WriteAllText(RecentFilePath, JsonSerializer.Serialize(_recentWorkspacePaths), Encoding.UTF8);
            }
            catch { }
        }

        private void LoadRecentWorkspaceList()
        {
            try
            {
                if (File.Exists(RecentFilePath))
                    _recentWorkspacePaths.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentFilePath))?.Where(File.Exists) ?? []);
            }
            catch { }
            RecentWorkspaceComboBox.ItemsSource = _recentWorkspacePaths.Select(IOPath.GetFileName).ToList();
            if (_recentWorkspacePaths.Count > 0) RecentWorkspaceComboBox.SelectedIndex = 0;
        }

        private void OpenRecentWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            int index = RecentWorkspaceComboBox.SelectedIndex;
            if (index < 0 || index >= _recentWorkspacePaths.Count) return;
            OpenWorkspaceDirect(_recentWorkspacePaths[index]);
        }

        private void SaveWorkspaceDirect(string path)
        {
            try
            {
                WorkspaceFile workspace = CaptureWorkspaceForSave();
                string json = JsonSerializer.Serialize(workspace, new JsonSerializerOptions { WriteIndented = true });
                string temp = path + ".tmp";
                File.WriteAllText(temp, json, Encoding.UTF8);
                File.Move(temp, path, true);
                _currentWorkspacePath = path;
                _projectDirty = false;
                UpdateProjectDirtyUi();
                RememberRecentWorkspace(path);
                if (File.Exists(RecoveryFilePath)) File.Delete(RecoveryFilePath);
                RecoveryButton.Visibility = Visibility.Collapsed;
                GraphStatusText.Text = "Workspace saved";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save workspace", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OpenWorkspaceDirect(string path)
        {
            try
            {
                LoadWorkspaceFromPath(path);
                _currentWorkspacePath = path;
                _projectDirty = false;
                UpdateProjectDirtyUi();
                RememberRecentWorkspace(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open recent workspace", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ----- shared project assets -----------------------------------------------------------

        private void PublishExpressionAssetButton_Click(object sender, RoutedEventArgs e)
        {
            GraphExpression? expression = _activeExpressionBox?.DataContext as GraphExpression ?? Expressions.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Expression));
            if (expression == null || string.IsNullOrWhiteSpace(expression.Expression)) return;
            string name = NormalizeAssetName(SharedAssetNameTextBox.Text, $"Function{SharedAssets.Count + 1}");
            UpsertSharedAsset(new SharedAsset
            {
                Name = name,
                Kind = SharedAssetKind.Function,
                ParameterName = "x",
                Formula = expression.Expression,
                Description = "Published from Function Lab"
            });
        }

        private void PublishCurveAssetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveActiveCurveChannel();
            List<CurveKey> keys = CurveDesignerMath.Ordered(CurveKeys);
            if (keys.Count < 2) { CurveGraphStatusText.Text = "Create at least two keys first"; return; }
            string formula = CurveDesignerMath.BuildPiecewiseFormula(keys, _curveBeforeMode, _curveAfterMode);
            string name = NormalizeAssetName(SharedAssetNameTextBox.Text, _activeCurveChannel?.Name ?? $"Curve{SharedAssets.Count + 1}");
            UpsertSharedAsset(new SharedAsset
            {
                Name = name,
                Kind = SharedAssetKind.Curve,
                ParameterName = "x",
                Formula = formula,
                Description = "Published from Curve Designer",
                Points = CurveDesignerMath.Sample(keys, 24).Select(p => new SharedAssetPoint(p.X, p.Y)).ToList()
            });
            CurveGraphStatusText.Text = $"Published {name}(x) as a shared asset";
        }

        private void UpsertSharedAsset(SharedAsset asset)
        {
            SharedAsset? existing = SharedAssets.FirstOrDefault(a => a.Name.Equals(asset.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) SharedAssets.Remove(existing);
            asset.PropertyChanged += (_, _) => { MarkProjectDirty(); RefreshSharedAssetConsumers(); };
            SharedAssets.Add(asset);
            SharedAssetsListBox.SelectedItem = asset;
            SharedAssetNameTextBox.Text = asset.Name;
            MarkProjectDirty();
            RefreshSharedAssetConsumers();
        }

        private void SharedAssetDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (SharedAssetsListBox.SelectedItem is not SharedAsset asset) return;
            SharedAssets.Remove(asset);
            MarkProjectDirty(); RefreshSharedAssetConsumers();
        }

        private void SharedAssetInsertButton_Click(object sender, RoutedEventArgs e)
        {
            if (SharedAssetsListBox.SelectedItem is not SharedAsset asset) return;
            string text = $"{asset.Name}(x)";
            GraphExpression item = AddExpression(text);
            SetWorkspaceMode(WorkspaceMode.FunctionLab, true);
            Dispatcher.BeginInvoke(new Action(() => FocusExpression(item)));
        }

        private void ImportTableButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Import two-column table", Filter = "CSV / text table (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                List<SharedAssetPoint> rows = [];
                foreach (string raw in File.ReadLines(dialog.FileName))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    string[] parts = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length < 2) continue;
                    if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
                    rows.Add(new SharedAssetPoint(x, y));
                }
                rows = rows.OrderBy(r => r.X).ToList();
                if (rows.Count < 2) throw new InvalidDataException("The file needs at least two numeric rows.");
                string name = NormalizeAssetName(IOPath.GetFileNameWithoutExtension(dialog.FileName), $"Table{ImportedTables.Count + 1}");
                var table = new ImportedTable { Name = name, Rows = rows };
                ImportedTables.Add(table);
                UpsertSharedAsset(new SharedAsset { Name = name, Kind = SharedAssetKind.Table, ParameterName = "x", Formula = BuildLinearTableFormula(rows), Points = rows, Description = IOPath.GetFileName(dialog.FileName) });
                ImportedTablesListBox.SelectedItem = table;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not import table", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private static string BuildLinearTableFormula(IReadOnlyList<SharedAssetPoint> rows)
        {
            if (rows.Count == 0) return "0";
            if (rows.Count == 1) return rows[0].Y.ToString("R", CultureInfo.InvariantCulture);
            string expression = rows[^1].Y.ToString("R", CultureInfo.InvariantCulture);
            for (int i = rows.Count - 2; i >= 0; i--)
            {
                SharedAssetPoint a = rows[i], b = rows[i + 1];
                double dx = b.X - a.X;
                double slope = Math.Abs(dx) < 1e-12 ? 0 : (b.Y - a.Y) / dx;
                string seg = $"({a.Y.ToString("R", CultureInfo.InvariantCulture)})+({slope.ToString("R", CultureInfo.InvariantCulture)})*(x-({a.X.ToString("R", CultureInfo.InvariantCulture)}))";
                expression = $"if(x<{b.X.ToString("R", CultureInfo.InvariantCulture)},{seg},{expression})";
            }
            return $"if(x<={rows[0].X.ToString("R", CultureInfo.InvariantCulture)},{rows[0].Y.ToString("R", CultureInfo.InvariantCulture)},{expression})";
        }

        private static string NormalizeAssetName(string? requested, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();
            value = Regex.Replace(value, "[^A-Za-z0-9_]", string.Empty);
            if (value.Length == 0) value = fallback;
            if (char.IsDigit(value[0])) value = "A" + value;
            return value;
        }

        private string ExpandSharedAssets(string expression, int depth = 0)
        {
            if (depth > 6 || string.IsNullOrWhiteSpace(expression) || SharedAssets.Count == 0) return expression;
            string result = expression;
            foreach (SharedAsset asset in SharedAssets.OrderByDescending(a => a.Name.Length))
            {
                int searchFrom = 0;
                while (searchFrom < result.Length)
                {
                    int nameIndex = result.IndexOf(asset.Name, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (nameIndex < 0) break;
                    int open = nameIndex + asset.Name.Length;
                    if (open >= result.Length || result[open] != '(') { searchFrom = open; continue; }
                    if (nameIndex > 0 && (char.IsLetterOrDigit(result[nameIndex - 1]) || result[nameIndex - 1] == '_')) { searchFrom = open; continue; }
                    int close = FindMatchingParenthesis(result, open);
                    if (close < 0) break;
                    string argument = result[(open + 1)..close];
                    string body = Regex.Replace(asset.Formula, $@"\b{Regex.Escape(asset.ParameterName)}\b", $"({argument})", RegexOptions.IgnoreCase);
                    body = ExpandSharedAssets(body, depth + 1);
                    result = result[..nameIndex] + "(" + body + ")" + result[(close + 1)..];
                    searchFrom = nameIndex + body.Length + 2;
                }
            }
            return result;
        }

        private static int FindMatchingParenthesis(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) return i;
            }
            return -1;
        }

        private void RefreshSharedAssetConsumers()
        {
            foreach (GraphExpression item in Expressions) RefreshExpression(item);
            foreach (EconomyLink link in EconomyLinks) CompileEconomyLink(link);
            SyncParameters(); SyncEconomyParameters(); ScheduleRender(80);
        }

        // ----- curve channels -----------------------------------------------------------------

        private void SaveActiveCurveChannel()
        {
            if (_activeCurveChannel == null) return;
            _activeCurveChannel.Keys = CurveKeys.Select(CloneCurveKey).ToList();
        }

        private void LoadActiveCurveChannel()
        {
            CurveKeys.Clear();
            if (_activeCurveChannel == null) return;
            foreach (CurveKey key in _activeCurveChannel.Keys.Select(CloneCurveKey)) CurveKeys.Add(key);
            UpdateCurveDesignerFormula(); RefreshCurveSuggestions();
            if (_workspaceMode == WorkspaceMode.CurveDesigner) RenderCurveDesigner();
        }

        private static CurveKey CloneCurveKey(CurveKey key) => new()
        {
            Id = key.Id, X = key.X, Y = key.Y, InTangent = key.InTangent, OutTangent = key.OutTangent,
            InWeight = key.InWeight, OutWeight = key.OutWeight, LinkedTangents = key.LinkedTangents, TangentMode = key.TangentMode
        };

        private void CurveChannelsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingWorkspace || !IsLoaded) return;
            SaveActiveCurveChannel();
            _activeCurveChannel = CurveChannelsComboBox.SelectedItem as CurveChannel;
            if (CurveChannelNameTextBox != null) CurveChannelNameTextBox.Text = _activeCurveChannel?.Name ?? string.Empty;
            LoadActiveCurveChannel();
        }

        private void CurveAddChannelButton_Click(object sender, RoutedEventArgs e)
        {
            SaveActiveCurveChannel();
            var channel = new CurveChannel { Name = $"Channel {CurveChannels.Count + 1}", ColorHex = CurveChannelColor(CurveChannels.Count) };
            CurveChannels.Add(channel); CurveChannelsComboBox.SelectedItem = channel;
            MarkWorkspaceEdit(); MarkProjectDirty();
        }

        private void CurveDeleteChannelButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurveChannels.Count <= 1 || CurveChannelsComboBox.SelectedItem is not CurveChannel channel) return;
            CurveChannels.Remove(channel);
            _activeCurveChannel = CurveChannels.FirstOrDefault();
            CurveChannelsComboBox.SelectedItem = _activeCurveChannel;
            LoadActiveCurveChannel(); MarkWorkspaceEdit(); MarkProjectDirty();
        }

        private void CurveChannelNameTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_activeCurveChannel == null) return;
            _activeCurveChannel.Name = CurveChannelNameTextBox.Text;
            CurveChannelsComboBox.Items.Refresh(); MarkProjectDirty();
        }

        private void CurveTensionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _curveAutoTension = Math.Clamp(e.NewValue, 0, 1);
            CurveDesignerMath.AutoTension = _curveAutoTension;
            if (CurveTensionText != null) CurveTensionText.Text = _curveAutoTension.ToString("0.00", CultureInfo.InvariantCulture);
            if (_workspaceMode == WorkspaceMode.CurveDesigner) { UpdateCurveDesignerFormula(); RenderCurveDesigner(); }
            MarkProjectDirty();
        }

        private static string CurveChannelColor(int index)
        {
            string[] colors = ["#4169E1", "#DC143C", "#2E8B57", "#FF8C00", "#9370DB", "#008B8B", "#FF1493"];
            return colors[index % colors.Length];
        }

        private Brush CurveChannelBrush(CurveChannel channel)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(channel.ColorHex)); }
            catch { return Brushes.SlateBlue; }
        }

        private void DrawInactiveCurveChannels(double width, double height)
        {
            SaveActiveCurveChannel();
            foreach (CurveChannel channel in CurveChannels.Where(c => c.IsVisible && !ReferenceEquals(c, _activeCurveChannel)))
            {
                List<CurveKey> keys = CurveDesignerMath.Ordered(channel.Keys);
                if (keys.Count < 2 || !CurveDesignerMath.HasDistinctX(keys)) continue;
                DrawSeries(CurveDesignerMath.SampleRange(keys, _viewport.MinX, _viewport.MaxX, _curveBeforeMode, _curveAfterMode, 420), CurveChannelBrush(channel), _viewport, width, height);
            }
        }

        // ----- economy authoring ---------------------------------------------------------------

        private void EconomyAddActionButton_Click(object sender, RoutedEventArgs e) => AddEconomyNode(EconomyNodeKind.Action);

        private void EconomyTriggerActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEconomyNode?.Kind != EconomyNodeKind.Action) return;
            _selectedEconomyNode.ActionQueued = true;
            EconomyFlowStatusText.Text = $"Queued action: {_selectedEconomyNode.Name}";
            if (!_economyPlaying) EconomySimulationStep(ReadEconomyDt(), render: true);
        }

        private void EconomySelectNodeForMultiEdit(EconomyNode node)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (!ctrl) _economySelectedNodeIds.Clear();
            if (ctrl && _economySelectedNodeIds.Contains(node.Id)) _economySelectedNodeIds.Remove(node.Id);
            else _economySelectedNodeIds.Add(node.Id);
            _selectedEconomyNode = node;
            UpdateEconomyInspector();
        }

        private IReadOnlyList<EconomyNode> SelectedEconomyNodes()
        {
            if (_economySelectedNodeIds.Count == 0 && _selectedEconomyNode != null) return [_selectedEconomyNode];
            return EconomyNodes.Where(n => _economySelectedNodeIds.Contains(n.Id)).ToList();
        }

        private void EconomyCopySelectedButton_Click(object sender, RoutedEventArgs e) => CopyEconomySelection();
        private void EconomyPasteSelectedButton_Click(object sender, RoutedEventArgs e) => PasteEconomySelection();
        private void EconomySelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            _economySelectedNodeIds.Clear();
            foreach (EconomyNode node in EconomyNodes.Where(n => !IsEconomyNodeHidden(n))) _economySelectedNodeIds.Add(node.Id);
            _selectedEconomyNode = EconomyNodes.FirstOrDefault(n => _economySelectedNodeIds.Contains(n.Id));
            RenderEconomyDiagram(); UpdateEconomyInspector();
        }

        private void CopyEconomySelection()
        {
            IReadOnlyList<EconomyNode> selected = SelectedEconomyNodes();
            if (selected.Count == 0) return;
            var ids = selected.Select(n => n.Id).ToHashSet();
            _economyClipboardNodes.Clear();
            _economyClipboardLinks.Clear();
            foreach (EconomyNode n in selected)
            {
                _economyClipboardNodes.Add(new EconomyNode
                {
                    Id = n.Id, Kind = n.Kind, Name = n.Name, Resource = n.Resource, Notes = n.Notes,
                    X = n.X, Y = n.Y, InitialAmount = n.InitialAmount, Amount = n.Amount, Capacity = n.Capacity,
                    SubsystemId = n.SubsystemId, ActionQueued = false
                });
            }
            foreach (EconomyLink l in EconomyLinks.Where(l => ids.Contains(l.SourceId) && ids.Contains(l.TargetId)))
            {
                _economyClipboardLinks.Add(new EconomyLink
                {
                    Id = l.Id, SourceId = l.SourceId, TargetId = l.TargetId, Label = l.Label,
                    RateExpression = l.RateExpression, ConditionExpression = l.ConditionExpression,
                    Efficiency = l.Efficiency, Chance = l.Chance, Interval = l.Interval, Delay = l.Delay,
                    StartTime = l.StartTime, EndTime = l.EndTime, IsEnabled = l.IsEnabled
                });
            }
            EconomyConnectHintText.Text = $"Copied {selected.Count} node(s). Ctrl+V pastes a new copy.";
        }

        private void PasteEconomySelection()
        {
            if (_economyClipboardNodes.Count == 0) return;
            var map = new Dictionary<Guid, EconomyNode>();
            foreach (EconomyNode source in _economyClipboardNodes)
            {
                var copy = new EconomyNode
                {
                    Kind = source.Kind, Name = source.Name + " copy", Resource = source.Resource, Notes = source.Notes,
                    X = source.X + 32, Y = source.Y + 32, InitialAmount = source.InitialAmount,
                    Amount = source.InitialAmount, Capacity = source.Capacity, SubsystemId = null
                };
                EconomyNodes.Add(copy);
                map[source.Id] = copy;
            }
            foreach (EconomyLink source in _economyClipboardLinks)
            {
                if (!map.TryGetValue(source.SourceId, out EconomyNode? a) || !map.TryGetValue(source.TargetId, out EconomyNode? b)) continue;
                EconomyLinks.Add(new EconomyLink
                {
                    SourceId = a.Id, TargetId = b.Id, Label = source.Label,
                    RateExpression = source.RateExpression, ConditionExpression = source.ConditionExpression,
                    Efficiency = source.Efficiency, Chance = source.Chance, Interval = source.Interval, Delay = source.Delay,
                    StartTime = source.StartTime, EndTime = source.EndTime, IsEnabled = source.IsEnabled
                });
            }
            _economySelectedNodeIds.Clear();
            foreach (EconomyNode node in map.Values) _economySelectedNodeIds.Add(node.Id);
            _selectedEconomyNode = map.Values.FirstOrDefault();
            SyncEconomyParameters();
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram(); UpdateEconomyInspector();
        }

        private void EconomyGroupSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<EconomyNode> nodes = SelectedEconomyNodes();
            if (nodes.Count < 2) { EconomyConnectHintText.Text = "Select two or more nodes (Ctrl+click) before grouping."; return; }
            var group = new EconomySubsystem { Name = $"Subsystem {EconomySubsystems.Count + 1}" };
            EconomySubsystems.Add(group);
            foreach (EconomyNode node in nodes) node.SubsystemId = group.Id;
            EconomySubsystemsListBox.SelectedItem = group;
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private void EconomyUngroupSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            EconomySubsystem? group = EconomySubsystemsListBox.SelectedItem as EconomySubsystem;
            if (group == null && _selectedEconomyNode?.SubsystemId is Guid id) group = EconomySubsystems.FirstOrDefault(g => g.Id == id);
            if (group == null) return;
            foreach (EconomyNode node in EconomyNodes.Where(n => n.SubsystemId == group.Id)) node.SubsystemId = null;
            EconomySubsystems.Remove(group);
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private void EconomyToggleSubsystemButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomySubsystemsListBox.SelectedItem is not EconomySubsystem group) return;
            group.IsCollapsed = !group.IsCollapsed;
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private void EconomyDuplicateSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<EconomyNode> selected = SelectedEconomyNodes();
            if (selected.Count == 0) return;
            var map = new Dictionary<Guid, EconomyNode>();
            foreach (EconomyNode source in selected)
            {
                var copy = new EconomyNode
                {
                    Kind = source.Kind, Name = source.Name + " copy", Resource = source.Resource,
                    Notes = source.Notes, X = source.X + 28, Y = source.Y + 28,
                    InitialAmount = source.InitialAmount, Amount = source.InitialAmount, Capacity = source.Capacity,
                    SubsystemId = source.SubsystemId
                };
                EconomyNodes.Add(copy); map[source.Id] = copy;
            }
            foreach (EconomyLink link in EconomyLinks.Where(l => map.ContainsKey(l.SourceId) && map.ContainsKey(l.TargetId)).ToList())
            {
                EconomyLinks.Add(new EconomyLink
                {
                    SourceId = map[link.SourceId].Id, TargetId = map[link.TargetId].Id,
                    Label = link.Label, RateExpression = link.RateExpression, ConditionExpression = link.ConditionExpression,
                    Efficiency = link.Efficiency, Chance = link.Chance, Interval = link.Interval, Delay = link.Delay,
                    StartTime = link.StartTime, EndTime = link.EndTime, IsEnabled = link.IsEnabled
                });
            }
            _economySelectedNodeIds.Clear(); foreach (EconomyNode n in map.Values) _economySelectedNodeIds.Add(n.Id);
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private void EconomyAlignHorizontalButton_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<EconomyNode> nodes = SelectedEconomyNodes(); if (nodes.Count < 2) return;
            double y = nodes.Average(n => n.Y); foreach (EconomyNode n in nodes) n.Y = y;
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private void EconomyAlignVerticalButton_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<EconomyNode> nodes = SelectedEconomyNodes(); if (nodes.Count < 2) return;
            double x = nodes.Average(n => n.X); foreach (EconomyNode n in nodes) n.X = x;
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private void EconomyDistributeButton_Click(object sender, RoutedEventArgs e)
        {
            List<EconomyNode> nodes = SelectedEconomyNodes().OrderBy(n => n.X).ToList(); if (nodes.Count < 3) return;
            double first = nodes[0].X, last = nodes[^1].X;
            for (int i = 1; i < nodes.Count - 1; i++) nodes[i].X = first + (last - first) * i / (nodes.Count - 1.0);
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private void EconomySnapSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            double step = 20;
            if (double.TryParse(EconomyGridStepTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0) step = parsed;
            foreach (EconomyNode n in SelectedEconomyNodes()) { n.X = Math.Round(n.X / step) * step; n.Y = Math.Round(n.Y / step) * step; }
            MarkWorkspaceEdit(); MarkProjectDirty(); RenderEconomyDiagram();
        }

        private bool IsEconomyNodeHidden(EconomyNode node) => node.SubsystemId is Guid id && EconomySubsystems.FirstOrDefault(g => g.Id == id)?.IsCollapsed == true;

        private bool TryGetEconomyVisualAnchor(EconomyNode node, out double x, out double y, out string visualName)
        {
            if (node.SubsystemId is Guid id && EconomySubsystems.FirstOrDefault(g => g.Id == id) is EconomySubsystem group && group.IsCollapsed)
            {
                List<EconomyNode> members = EconomyNodes.Where(n => n.SubsystemId == group.Id).ToList();
                if (members.Count > 0)
                {
                    double minX = members.Min(n => n.X), minY = members.Min(n => n.Y);
                    x = minX + 80; y = minY + 32; visualName = group.Name;
                    return true;
                }
            }
            x = node.X + 68; y = node.Y + 40; visualName = node.Name;
            return true;
        }

        private bool EconomyLinkIsInternalToCollapsedSubsystem(EconomyNode source, EconomyNode target)
        {
            if (source.SubsystemId is not Guid a || target.SubsystemId is not Guid b || a != b) return false;
            return EconomySubsystems.FirstOrDefault(g => g.Id == a)?.IsCollapsed == true;
        }

        private void AddEconomyPorts(EconomyNode node, Brush resourceBrush, double nodeWidth, double nodeHeight)
        {
            bool hasInput = EconomyLinks.Any(l => l.TargetId == node.Id) || node.Kind is EconomyNodeKind.Pool or EconomyNodeKind.Converter or EconomyNodeKind.Gate or EconomyNodeKind.Queue or EconomyNodeKind.Register or EconomyNodeKind.Sink;
            bool hasOutput = EconomyLinks.Any(l => l.SourceId == node.Id) || node.Kind is EconomyNodeKind.Source or EconomyNodeKind.Event or EconomyNodeKind.Pool or EconomyNodeKind.Converter or EconomyNodeKind.Gate or EconomyNodeKind.Queue or EconomyNodeKind.Register or EconomyNodeKind.Action;
            if (hasInput)
            {
                var input = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = resourceBrush, StrokeThickness = 2, IsHitTestVisible = false, ToolTip = "Typed input port" };
                Canvas.SetLeft(input, node.X - 5); Canvas.SetTop(input, node.Y + nodeHeight / 2 - 5); EconomyCanvas.Children.Add(input);
            }
            if (hasOutput)
            {
                var output = new Ellipse { Width = 10, Height = 10, Fill = resourceBrush, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false, ToolTip = "Typed output port" };
                Canvas.SetLeft(output, node.X + nodeWidth - 5); Canvas.SetTop(output, node.Y + nodeHeight / 2 - 5); EconomyCanvas.Children.Add(output);
            }
        }

        private void DrawEconomySubsystemFrames()
        {
            foreach (EconomySubsystem group in EconomySubsystems)
            {
                List<EconomyNode> members = EconomyNodes.Where(n => n.SubsystemId == group.Id).ToList();
                if (members.Count == 0) continue;
                double minX = members.Min(n => n.X), minY = members.Min(n => n.Y), maxX = members.Max(n => n.X + 136), maxY = members.Max(n => n.Y + 80);
                if (group.IsCollapsed)
                {
                    var box = new Border { Width = 160, Height = 64, Background = new SolidColorBrush(Color.FromArgb(245, 245, 247, 250)), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(9), Padding = new Thickness(8), Tag = group };
                    box.Child = new StackPanel { Children = { new TextBlock { Text = group.Name, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = $"{members.Count} nodes · collapsed", FontSize = 10, Foreground = Brushes.DimGray } } };
                    Canvas.SetLeft(box, minX); Canvas.SetTop(box, minY); EconomyCanvas.Children.Add(box);

                    var memberIds = members.Select(n => n.Id).ToHashSet();
                    var inputs = EconomyLinks.Where(l => memberIds.Contains(l.TargetId) && !memberIds.Contains(l.SourceId))
                        .Select(l => EconomyNodes.FirstOrDefault(n => n.Id == l.SourceId)?.Resource).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var outputs = EconomyLinks.Where(l => memberIds.Contains(l.SourceId) && !memberIds.Contains(l.TargetId))
                        .Select(l => EconomyNodes.FirstOrDefault(n => n.Id == l.SourceId)?.Resource).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    for (int i = 0; i < inputs.Count; i++)
                    {
                        string resource = inputs[i]!;
                        var port = new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Stroke = EconomyResourceBrush(resource), StrokeThickness = 2, ToolTip = $"Input: {resource}", IsHitTestVisible = false };
                        Canvas.SetLeft(port, minX - 4.5); Canvas.SetTop(port, minY + 17 + i * 13); EconomyCanvas.Children.Add(port);
                    }
                    for (int i = 0; i < outputs.Count; i++)
                    {
                        string resource = outputs[i]!;
                        var port = new Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Stroke = EconomyResourceBrush(resource), StrokeThickness = 2, ToolTip = $"Output: {resource}", IsHitTestVisible = false };
                        Canvas.SetLeft(port, minX + 155.5); Canvas.SetTop(port, minY + 17 + i * 13); EconomyCanvas.Children.Add(port);
                    }
                }
                else
                {
                    var rect = new Border { Width = maxX - minX + 28, Height = maxY - minY + 42, Background = new SolidColorBrush(Color.FromArgb(18, 80, 100, 130)), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 100, 116, 139)), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10), IsHitTestVisible = false };
                    Canvas.SetLeft(rect, minX - 14); Canvas.SetTop(rect, minY - 28); EconomyCanvas.Children.Add(rect);
                    var label = new TextBlock { Text = group.Name, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.SlateGray, IsHitTestVisible = false };
                    Canvas.SetLeft(label, minX - 7); Canvas.SetTop(label, minY - 23); EconomyCanvas.Children.Add(label);
                }
            }
        }

        private void EconomyCreateRecipeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEconomyNode?.Kind != EconomyNodeKind.Converter) { EconomyFlowStatusText.Text = "Select a Converter first."; return; }
            EconomyNode converter = _selectedEconomyNode;
            var incoming = EconomyLinks.Where(l => l.TargetId == converter.Id).ToList();
            var outgoing = EconomyLinks.Where(l => l.SourceId == converter.Id).ToList();
            if (incoming.Count == 0 || outgoing.Count == 0) { EconomyFlowStatusText.Text = "A recipe needs at least one incoming and one outgoing connection."; return; }
            var recipe = new EconomyRecipe { ConverterNodeId = converter.Id, Name = converter.Name + " recipe", CraftRateExpression = EconomyRecipeRateTextBox.Text.Trim() is { Length: > 0 } t ? t : "1" };
            foreach (EconomyLink link in incoming)
            {
                EconomyNode? source = EconomyNodes.FirstOrDefault(n => n.Id == link.SourceId); if (source == null) continue;
                recipe.Inputs.Add(new EconomyRecipeItem { NodeId = source.Id, Resource = source.Resource, Amount = ParseRecipeAmount(link.RateExpression) });
                link.IsEnabled = false;
            }
            foreach (EconomyLink link in outgoing)
            {
                EconomyNode? target = EconomyNodes.FirstOrDefault(n => n.Id == link.TargetId); if (target == null) continue;
                recipe.Outputs.Add(new EconomyRecipeItem { NodeId = target.Id, Resource = target.Resource, Amount = ParseRecipeAmount(link.RateExpression) * Math.Max(0, link.Efficiency) });
                link.IsEnabled = false;
            }
            EconomyRecipes.Add(recipe); EconomyRecipesListBox.SelectedItem = recipe;
            MarkProjectDirty(); RenderEconomyDiagram();
        }

        private static double ParseRecipeAmount(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0 ? value : 1;

        private bool IsEconomyRecipeLink(EconomyLink link)
        {
            return EconomyRecipes.Any(recipe => recipe.IsEnabled &&
                ((link.TargetId == recipe.ConverterNodeId && recipe.Inputs.Any(i => i.NodeId == link.SourceId)) ||
                 (link.SourceId == recipe.ConverterNodeId && recipe.Outputs.Any(o => o.NodeId == link.TargetId))));
        }

        private void ProcessEconomyRecipes(double dt)
        {
            foreach (EconomyRecipe recipe in EconomyRecipes.Where(r => r.IsEnabled).ToList())
            {
                EconomyNode? converter = EconomyNodes.FirstOrDefault(n => n.Id == recipe.ConverterNodeId);
                if (converter == null || recipe.Inputs.Count == 0 || recipe.Outputs.Count == 0) continue;
                double craftsPerSecond;
                try
                {
                    CalculatorEngine.CompiledExpression compiled = CalculatorEngine.Compile(ExpandSharedAssets(recipe.CraftRateExpression));
                    var vars = EconomyParameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                    vars["t"] = _economyTime; vars["time"] = _economyTime; vars["dt"] = dt;
                    craftsPerSecond = compiled.Evaluate(vars);
                }
                catch { continue; }
                if (!double.IsFinite(craftsPerSecond) || craftsPerSecond <= 0) continue;
                double crafts = craftsPerSecond * dt;
                foreach (EconomyRecipeItem input in recipe.Inputs)
                {
                    EconomyNode? node = EconomyNodes.FirstOrDefault(n => n.Id == input.NodeId);
                    if (node == null || input.Amount <= 0) { crafts = 0; break; }
                    crafts = Math.Min(crafts, node.Amount / input.Amount);
                }
                foreach (EconomyRecipeItem output in recipe.Outputs)
                {
                    EconomyNode? node = EconomyNodes.FirstOrDefault(n => n.Id == output.NodeId);
                    if (node?.IsStorage == true && output.Amount > 0) crafts = Math.Min(crafts, Math.Max(0, node.Capacity - node.Amount) / output.Amount);
                }
                if (crafts <= 1e-12) continue;
                foreach (EconomyRecipeItem input in recipe.Inputs)
                {
                    EconomyNode? node = EconomyNodes.FirstOrDefault(n => n.Id == input.NodeId);
                    if (node == null) continue;
                    double amount = input.Amount * crafts;
                    node.Amount = Math.Max(0, node.Amount - amount);
                    EconomyLink? visual = EconomyLinks.FirstOrDefault(l => l.SourceId == node.Id && l.TargetId == converter.Id);
                    if (visual != null)
                    {
                        _economyLastFlowRates[visual.Id] = amount / Math.Max(dt, 1e-12);
                        RegisterEconomyVisualTransfer(visual, amount);
                    }
                }
                foreach (EconomyRecipeItem output in recipe.Outputs)
                {
                    EconomyNode? node = EconomyNodes.FirstOrDefault(n => n.Id == output.NodeId);
                    if (node == null) continue;
                    double amount = output.Amount * crafts;
                    node.Amount = node.IsStorage ? Math.Min(node.Capacity, node.Amount + amount) : node.Amount + amount;
                    EconomyLink? visual = EconomyLinks.FirstOrDefault(l => l.SourceId == converter.Id && l.TargetId == node.Id);
                    if (visual != null)
                    {
                        _economyLastFlowRates[visual.Id] = amount / Math.Max(dt, 1e-12);
                        RegisterEconomyVisualTransfer(visual, amount);
                    }
                }
            }
        }

        private void EconomyDeleteRecipeButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyRecipesListBox.SelectedItem is EconomyRecipe recipe) { EconomyRecipes.Remove(recipe); MarkProjectDirty(); }
        }

        private void EnsureEconomyResourceStyles()
        {
            foreach (string resource in EconomyNodes.Select(n => n.Resource).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (EconomyResourceStyles.Any(s => s.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase))) continue;
                Color color = EconomyVisuals.ResourceColor(resource);
                EconomyResourceStyles.Add(new EconomyResourceStyle { Resource = resource, ColorHex = color.ToString(), Icon = "●" });
            }
        }

        private Brush EconomyResourceBrush(string resource, byte alpha = 255)
        {
            EnsureEconomyResourceStyles();
            EconomyResourceStyle? style = EconomyResourceStyles.FirstOrDefault(s => s.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase));
            if (style != null)
            {
                try
                {
                    Color c = (Color)ColorConverter.ConvertFromString(style.ColorHex); c.A = alpha;
                    var brush = new SolidColorBrush(c); brush.Freeze(); return brush;
                }
                catch { }
            }
            return EconomyVisuals.ResourceBrush(resource, alpha);
        }

        private void EconomyResourceStyleApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyResourceStylesListBox.SelectedItem is not EconomyResourceStyle style) return;
            style.ColorHex = EconomyResourceColorTextBox.Text;
            style.Icon = EconomyResourceIconTextBox.Text;
            style.Unit = EconomyResourceUnitTextBox.Text;
            MarkProjectDirty(); RenderEconomyDiagram(); RenderEconomyChart();
        }

        private void EconomyResourceStylesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EconomyResourceStylesListBox.SelectedItem is not EconomyResourceStyle style) return;
            EconomyResourceColorTextBox.Text = style.ColorHex;
            EconomyResourceIconTextBox.Text = style.Icon;
            EconomyResourceUnitTextBox.Text = style.Unit;
        }

        // ----- replay / debugger / sensitivity ------------------------------------------------

        private void EconomyReplaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || !_economyReplayMode || _economyHistory.Count == 0) return;
            _economyPlaying = false; _economyTimer.Stop();
            _economyReplayTime = e.NewValue;
            EconomyHistoryPoint frame = _economyHistory.OrderBy(h => Math.Abs(h.Time - _economyReplayTime)).First();
            _updatingEconomySimulation = true;
            try
            {
                foreach (EconomyNode node in EconomyNodes)
                    if (frame.Amounts.TryGetValue(node.Id, out double amount)) node.Amount = amount;
            }
            finally { _updatingEconomySimulation = false; }
            EconomyTimeText.Text = $"replay t = {frame.Time:0.###}";
            RenderEconomyDiagram(); RenderEconomyChart();
        }

        private void EconomyReplayToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _economyReplayMode = !_economyReplayMode;
            EconomyReplayToggleButton.Content = _economyReplayMode ? "Return live" : "Replay";
            EconomyReplaySlider.IsEnabled = _economyReplayMode;
            if (_economyReplayMode && _economyHistory.Count > 0)
            {
                EconomyReplaySlider.Minimum = _economyHistory.First().Time;
                EconomyReplaySlider.Maximum = _economyHistory.Last().Time;
                EconomyReplaySlider.Value = _economyHistory.Last().Time;
            }
            else
            {
                ResetEconomySimulation();
            }
        }

        private void EconomyBreakpointButton_Click(object sender, RoutedEventArgs e)
        {
            _economyBreakpointLinkId = _selectedEconomyLink?.Id;
            EconomyBreakpointText.Text = _selectedEconomyLink == null ? "No breakpoint" : $"Break: {_selectedEconomyLink.Display}";
        }

        private void RecordEconomyDebug(EconomyLink link, double requested, double accepted, double delivered, string reason, double conditionValue = 1, double chanceRoll = double.NaN, bool conditionPassed = true, bool chancePassed = true)
        {
            EconomyNode? source = EconomyNodes.FirstOrDefault(n => n.Id == link.SourceId);
            EconomyNode? target = EconomyNodes.FirstOrDefault(n => n.Id == link.TargetId);
            string flow = $"{source?.Name ?? "?"} → {target?.Name ?? "?"}";
            EconomyDebugEvents.Insert(0, new EconomyDebugEvent(_economyTime, link.Id, flow, requested, accepted, delivered, conditionPassed, chancePassed, conditionValue, chanceRoll, link.Chance, reason));
            while (EconomyDebugEvents.Count > 250) EconomyDebugEvents.RemoveAt(EconomyDebugEvents.Count - 1);
            if (_economyBreakpointLinkId == link.Id && accepted > 1e-10)
            {
                _economyPlaying = false; _economyTimer.Stop();
                EconomyBreakpointText.Text = $"Paused at t={_economyTime:0.###}: {flow}";
            }
        }

        private void EconomyClearDebuggerButton_Click(object sender, RoutedEventArgs e) => EconomyDebugEvents.Clear();

        private async void EconomySensitivityButton_Click(object sender, RoutedEventArgs e)
        {
            if (EconomyParameters.Count == 0 || EconomyNodes.Count == 0) { EconomySensitivityTextBox.Text = "No parameters to analyse."; return; }
            EconomySensitivityButton.IsEnabled = false;
            EconomySensitivityTextBox.Text = "Calculating local sensitivity…";
            try
            {
                double horizon = ReadEconomyPredictionHorizon(); double dt = ReadEconomyDt(); long seed = ReadEconomySeed();
                EconomySimulationModel model = EconomySimulationModel.Capture(EconomyNodes, EconomyLinks, EconomyParameters, EconomyRecipes);
                EconomyNode target = EconomyTargetNodeComboBox.SelectedItem as EconomyNode ?? EconomyNodes.First();
                string report = await System.Threading.Tasks.Task.Run(() =>
                {
                    var baselineOverrides = EconomyParameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                    double baseline = EconomySimulationEngine.Run(model, horizon, dt, seed, baselineOverrides).FinalAmounts.GetValueOrDefault(target.Id);
                    var rows = new List<(string name, double effect)>();
                    foreach (EconomyParameter p in EconomyParameters)
                    {
                        double span = Math.Max(1e-9, p.Maximum - p.Minimum); double delta = span * 0.05;
                        var values = new Dictionary<string, double>(baselineOverrides, StringComparer.OrdinalIgnoreCase) { [p.Name] = Math.Clamp(p.Value + delta, p.Minimum, p.Maximum) };
                        double changed = EconomySimulationEngine.Run(model, horizon, dt, seed, values).FinalAmounts.GetValueOrDefault(target.Id);
                        rows.Add((p.Name, (changed - baseline) / Math.Max(delta, 1e-9)));
                    }
                    var sb = new StringBuilder(); sb.AppendLine($"Target: {target.Name} @ t={horizon:G5}"); sb.AppendLine($"Baseline: {baseline:G8}\n");
                    foreach (var row in rows.OrderByDescending(r => Math.Abs(r.effect))) sb.AppendLine($"{row.name,-20} {row.effect,12:G6} output / parameter unit");
                    return sb.ToString();
                });
                EconomySensitivityTextBox.Text = report;
            }
            catch (Exception ex) { EconomySensitivityTextBox.Text = ex.Message; }
            finally { EconomySensitivityButton.IsEnabled = true; }
        }

        // ----- serialisation adapters ----------------------------------------------------------

        private static WorkspaceCurveChannel ToWorkspaceCurveChannel(CurveChannel c) => new()
        {
            Id = c.Id, Name = c.Name, ColorHex = c.ColorHex, IsVisible = c.IsVisible,
            Keys = c.Keys.Select(k => new WorkspaceCurveKey { Id = k.Id, X = k.X, Y = k.Y, InTangent = k.InTangent, OutTangent = k.OutTangent, InWeight = k.InWeight, OutWeight = k.OutWeight, LinkedTangents = k.LinkedTangents, TangentMode = k.TangentMode }).ToList()
        };
        private static CurveChannel FromWorkspaceCurveChannel(WorkspaceCurveChannel c) => new() { Id = c.Id == Guid.Empty ? Guid.NewGuid() : c.Id, Name = c.Name, ColorHex = c.ColorHex, IsVisible = c.IsVisible, Keys = c.Keys?.Select(k => new CurveKey { Id = k.Id, X = k.X, Y = k.Y, InTangent = k.InTangent, OutTangent = k.OutTangent, InWeight = k.InWeight <= 0 ? 1 : k.InWeight, OutWeight = k.OutWeight <= 0 ? 1 : k.OutWeight, LinkedTangents = k.LinkedTangents, TangentMode = k.TangentMode }).ToList() ?? [] };
        private static WorkspaceSharedAsset ToWorkspaceSharedAsset(SharedAsset a) => new() { Id = a.Id, Kind = a.Kind.ToString(), Name = a.Name, ParameterName = a.ParameterName, Formula = a.Formula, Description = a.Description, Unit = a.Unit, Points = a.Points.ToList() };
        private static SharedAsset FromWorkspaceSharedAsset(WorkspaceSharedAsset a) => new() { Id = a.Id == Guid.Empty ? Guid.NewGuid() : a.Id, Kind = Enum.TryParse(a.Kind, true, out SharedAssetKind k) ? k : SharedAssetKind.Function, Name = a.Name, ParameterName = a.ParameterName, Formula = a.Formula, Description = a.Description, Unit = a.Unit, Points = a.Points?.ToList() ?? [] };
        private static WorkspaceEconomySubsystem ToWorkspaceSubsystem(EconomySubsystem g) => new() { Id = g.Id, Name = g.Name, IsCollapsed = g.IsCollapsed, Notes = g.Notes };
        private static EconomySubsystem FromWorkspaceSubsystem(WorkspaceEconomySubsystem g) => new() { Id = g.Id == Guid.Empty ? Guid.NewGuid() : g.Id, Name = g.Name, IsCollapsed = g.IsCollapsed, Notes = g.Notes };
        private static WorkspaceEconomyResourceStyle ToWorkspaceResourceStyle(EconomyResourceStyle r) => new() { Resource = r.Resource, ColorHex = r.ColorHex, Icon = r.Icon, Unit = r.Unit };
        private static EconomyResourceStyle FromWorkspaceResourceStyle(WorkspaceEconomyResourceStyle r) => new() { Resource = r.Resource, ColorHex = r.ColorHex, Icon = r.Icon, Unit = r.Unit };
        private static WorkspaceEconomyRecipe ToWorkspaceRecipe(EconomyRecipe r) => new() { Id = r.Id, ConverterNodeId = r.ConverterNodeId, Name = r.Name, CraftRateExpression = r.CraftRateExpression, IsEnabled = r.IsEnabled, Inputs = r.Inputs.Select(i => new WorkspaceEconomyRecipeItem { NodeId = i.NodeId, Resource = i.Resource, Amount = i.Amount }).ToList(), Outputs = r.Outputs.Select(i => new WorkspaceEconomyRecipeItem { NodeId = i.NodeId, Resource = i.Resource, Amount = i.Amount }).ToList() };
        private static EconomyRecipe FromWorkspaceRecipe(WorkspaceEconomyRecipe r) => new() { Id = r.Id == Guid.Empty ? Guid.NewGuid() : r.Id, ConverterNodeId = r.ConverterNodeId, Name = r.Name, CraftRateExpression = r.CraftRateExpression, IsEnabled = r.IsEnabled, Inputs = r.Inputs?.Select(i => new EconomyRecipeItem { NodeId = i.NodeId, Resource = i.Resource, Amount = i.Amount }).ToList() ?? [], Outputs = r.Outputs?.Select(i => new EconomyRecipeItem { NodeId = i.NodeId, Resource = i.Resource, Amount = i.Amount }).ToList() ?? [] };
    }

    public sealed class IntegrationRecoveryState
    {
        public DateTime TimestampUtc { get; set; }
        public string WorkspaceMode { get; set; } = "FunctionLab";
        public string HlslScratchText { get; set; } = string.Empty;
        public List<WorkspaceExpression> Expressions { get; set; } = [];
        public List<WorkspaceParameter> Parameters { get; set; } = [];
        public string CurveBeforeMode { get; set; } = "Clamp";
        public string CurveAfterMode { get; set; } = "Clamp";
        public double CurveAutoTension { get; set; }
        public List<WorkspaceCurveChannel> CurveChannels { get; set; } = [];
        public Guid ActiveCurveChannelId { get; set; }
        public List<WorkspaceSharedAsset> SharedAssets { get; set; } = [];
        public List<WorkspaceImportedTable> ImportedTables { get; set; } = [];
        public List<WorkspaceEconomyNode> EconomyNodes { get; set; } = [];
        public List<WorkspaceEconomyLink> EconomyLinks { get; set; } = [];
        public List<WorkspaceEconomyParameter> EconomyParameters { get; set; } = [];
        public List<WorkspaceEconomyScenario> EconomyScenarios { get; set; } = [];
        public List<WorkspaceEconomyCohort> EconomyCohorts { get; set; } = [];
        public List<WorkspaceEconomyTarget> EconomyTargets { get; set; } = [];
        public List<WorkspaceEconomySubsystem> EconomySubsystems { get; set; } = [];
        public List<WorkspaceEconomyRecipe> EconomyRecipes { get; set; } = [];
        public List<WorkspaceEconomyResourceStyle> EconomyResourceStyles { get; set; } = [];
        public double EconomyTime { get; set; }
    }
}
