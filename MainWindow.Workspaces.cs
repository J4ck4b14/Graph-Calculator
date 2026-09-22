using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GraphCalculator
{
    public partial class MainWindow
    {
        private WorkspaceMode _workspaceMode = WorkspaceMode.FunctionLab;

        public ObservableCollection<CurveKey> CurveKeys { get; } = new();
        public ObservableCollection<EconomyNode> EconomyNodes { get; } = new();
        public ObservableCollection<EconomyLink> EconomyLinks { get; } = new();
        public ObservableCollection<EconomyParameter> EconomyParameters { get; } = new();
        public ObservableCollection<EconomyScenario> EconomyScenarios { get; } = new();
        public ObservableCollection<EconomyCohort> EconomyCohorts { get; } = new();
        public ObservableCollection<EconomyTarget> EconomyTargets { get; } = new();

        private void InitializeWorkspaceModes()
        {
            InitializeCurveDesigner();
            InitializeEconomyDesigner();
            SetWorkspaceMode(_workspaceMode, updateCombo: true);
        }

        private void WorkspaceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _loadingWorkspace) return;
            WorkspaceMode mode = WorkspaceModeComboBox.SelectedIndex switch
            {
                1 => WorkspaceMode.CurveDesigner,
                2 => WorkspaceMode.EconomyDesigner,
                3 => WorkspaceMode.HlslLab,
                _ => WorkspaceMode.FunctionLab
            };
            SetWorkspaceMode(mode, updateCombo: false);
        }

        private void SetWorkspaceMode(WorkspaceMode mode, bool updateCombo)
        {
            _workspaceMode = mode;
            if (updateCombo && WorkspaceModeComboBox != null)
                WorkspaceModeComboBox.SelectedIndex = mode switch
                {
                    WorkspaceMode.CurveDesigner => 1,
                    WorkspaceMode.EconomyDesigner => 2,
                    WorkspaceMode.HlslLab => 3,
                    _ => 0
                };

            bool function = mode == WorkspaceMode.FunctionLab;
            bool curve = mode == WorkspaceMode.CurveDesigner;
            bool economy = mode == WorkspaceMode.EconomyDesigner;
            bool hlsl = mode == WorkspaceMode.HlslLab;
            if (!economy)
            {
                _economyPlaying = false;
                _economyTimer.Stop();
            }

            CurveDesignerLeftPanel.Visibility = curve ? Visibility.Visible : Visibility.Collapsed;
            EconomyDesignerLeftPanel.Visibility = economy ? Visibility.Visible : Visibility.Collapsed;
            HlslLabLeftPanel.Visibility = hlsl ? Visibility.Visible : Visibility.Collapsed;
            EconomyWorkspacePanel.Visibility = economy ? Visibility.Visible : Visibility.Collapsed;
            HlslWorkspacePanel.Visibility = hlsl ? Visibility.Visible : Visibility.Collapsed;
            OnHlslWorkspaceVisibilityChanged(hlsl);

            FunctionGraphToolbar.Visibility = function ? Visibility.Visible : Visibility.Collapsed;
            CurveGraphToolbar.Visibility = curve ? Visibility.Visible : Visibility.Collapsed;
            GraphToolsTabs.Visibility = (function || curve) ? Visibility.Visible : Visibility.Collapsed;
            TimelinePanel.Visibility = function ? Visibility.Visible : Visibility.Collapsed;
            AnalysisToolsPanel.Visibility = function ? Visibility.Visible : Visibility.Collapsed;

            if (function)
            {
                GraphHeadingText.Text = "Graph";
                InteractionHintText.Text = "Scroll to zoom · drag to pan";
                PlotRangePanel.Visibility = _is3DMode ? Visibility.Collapsed : Visibility.Visible;
                SurfaceRangePanel.Visibility = _is3DMode ? Visibility.Visible : Visibility.Collapsed;
                PlotCanvas.Cursor = Cursors.Arrow;
                ScheduleRender(0);
            }
            else if (curve)
            {
                _is3DMode = false;
                if (PlotModeComboBox != null) PlotModeComboBox.SelectedIndex = 0;
                GraphHeadingText.Text = "Curve Designer";
                PlotRangePanel.Visibility = Visibility.Visible;
                SurfaceRangePanel.Visibility = Visibility.Collapsed;
                SurfaceViewport3D.Visibility = Visibility.Collapsed;
                PlotCanvas.Visibility = Visibility.Visible;
                SurfaceLegend.Visibility = Visibility.Collapsed;
                InteractionHintText.Text = "Click to add · drag keys/handles · Alt+drag to pan · wheel to zoom";
                RenderCurveDesigner();
            }
            else if (economy)
            {
                GraphHeadingText.Text = "Economy Designer";
                InteractionHintText.Text = "Drag nodes · connect flows · formulas drive rates";
                HideHover();
                RenderEconomyDiagram();
                RenderEconomyChart();
                UpdateEconomyDiagnostics();
            }
            else
            {
                GraphHeadingText.Text = "HLSL Lab";
                PlotRangePanel.Visibility = Visibility.Collapsed;
                SurfaceRangePanel.Visibility = Visibility.Collapsed;
                SurfaceViewport3D.Visibility = Visibility.Collapsed;
                PlotCanvas.Visibility = Visibility.Visible;
                SurfaceLegend.Visibility = Visibility.Collapsed;
                InteractionHintText.Text = "Write HLSL → Compile & Run → inspect real compiler output and live shader";
                RefreshHlslPreviewIfEmpty();
                if (HlslWorkspaceTabControl != null && HlslWorkspaceTabControl.SelectedIndex < 0) HlslWorkspaceTabControl.SelectedIndex = 0;
            }

            EnsureGraphToolsTabSelection();
            UpdateDynamicsInspector();

            FooterHintText.Text = mode switch
            {
                WorkspaceMode.CurveDesigner => "Visual curve asset · Bézier-style tangents · extrapolation · analytic approximation suggestions",
                WorkspaceMode.EconomyDesigner => "Typed flows · scenarios · stochastic prediction · portable .gceconomy projects",
                WorkspaceMode.HlslLab => "Editable HLSL · real ps_3_0 compile diagnostics · live GPU preview · searchable shader reference",
                _ => "Enter adds a line · Ctrl+Enter refreshes immediately · Ctrl+Space suggests functions · Esc clears the active expression"
            };
        }
    }
}
