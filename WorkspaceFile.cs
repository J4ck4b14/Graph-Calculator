using System;
using System.Collections.Generic;

namespace GraphCalculator
{
    public sealed class WorkspaceFile
    {
        public int Version { get; set; } = 9;
        public string WorkspaceMode { get; set; } = "FunctionLab";
        public bool Is3DMode { get; set; }
        public PlotViewport PlotViewport { get; set; } = new(-10, 10, -10, 10);
        public SurfaceViewport SurfaceViewport { get; set; } = new(-10, 10, -10, 10, -10, 10);
        public double SurfaceYaw { get; set; } = 38;
        public double SurfacePitch { get; set; } = 28;
        public double SurfaceCameraDistance { get; set; } = 18;
        public string SurfaceDisplayMode { get; set; } = "Solid";
        public double TimelineStart { get; set; } = 0;
        public double TimelineEnd { get; set; } = 10;
        public double TimelineTime { get; set; } = 0;
        public double TimelineSpeed { get; set; } = 1;
        public bool TimelineLoop { get; set; } = true;
        public string HlslScratchText { get; set; } = string.Empty;
        public bool ShowDerivative { get; set; }
        public bool ComparisonEnabled { get; set; }
        public string ComparisonMode { get; set; } = "A - B";
        public int ComparisonAIndex { get; set; }
        public int ComparisonBIndex { get; set; } = 1;
        public bool CrossSectionEnabled { get; set; }
        public string CrossSectionAxis { get; set; } = "Y";
        public string CrossSectionValue { get; set; } = "0";
        public int CrossSectionExpressionIndex { get; set; }
        public string RenderQuality { get; set; } = "Normal";
        public bool FieldPreviewEnabled { get; set; }
        public int FieldPreviewExpressionIndex { get; set; }
        public string FieldPreviewPalette { get; set; } = "Signed";
        public Dictionary<string, double> ParameterStateA { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> ParameterStateB { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public double ParameterStateBlend { get; set; }
        public List<WorkspaceExpression> Expressions { get; set; } = [];
        public List<WorkspaceParameter> Parameters { get; set; } = [];
        public List<WorkspaceCurveKey> CurveKeys { get; set; } = [];
        public List<WorkspaceCurveChannel> CurveChannels { get; set; } = [];
        public Guid ActiveCurveChannelId { get; set; }
        public double CurveAutoTension { get; set; } = 0.5;
        public List<WorkspaceSharedAsset> SharedAssets { get; set; } = [];
        public List<WorkspaceImportedTable> ImportedTables { get; set; } = [];
        public string CurveBeforeMode { get; set; } = "Clamp";
        public string CurveAfterMode { get; set; } = "Clamp";
        public double EconomyTime { get; set; }
        public double EconomyTimeStep { get; set; } = 0.1;
        public double EconomyPlaybackSpeed { get; set; } = 1;
        public long EconomySeed { get; set; } = 1337;
        public int EconomyPredictionRuns { get; set; } = 500;
        public double EconomyPredictionHorizon { get; set; } = 30;
        public string EconomyProjectName { get; set; } = "Economy model";
        public string EconomyProjectDescription { get; set; } = string.Empty;
        public List<WorkspaceEconomyNode> EconomyNodes { get; set; } = [];
        public List<WorkspaceEconomyLink> EconomyLinks { get; set; } = [];
        public List<WorkspaceEconomyParameter> EconomyParameters { get; set; } = [];
        public List<WorkspaceEconomyScenario> EconomyScenarios { get; set; } = [];
        public List<WorkspaceEconomyCohort> EconomyCohorts { get; set; } = [];
        public List<WorkspaceEconomyTarget> EconomyTargets { get; set; } = [];
        public List<WorkspaceEconomySubsystem> EconomySubsystems { get; set; } = [];
        public List<WorkspaceEconomyRecipe> EconomyRecipes { get; set; } = [];
        public List<WorkspaceEconomyResourceStyle> EconomyResourceStyles { get; set; } = [];
    }

    public sealed class WorkspaceExpression
    {
        public string Expression { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
        public string DomainMinX { get; set; } = string.Empty;
        public string DomainMaxX { get; set; } = string.Empty;
        public string DomainMinY { get; set; } = string.Empty;
        public string DomainMaxY { get; set; } = string.Empty;
    }

    public sealed class WorkspaceParameter
    {
        public string Name { get; set; } = string.Empty;
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Value { get; set; }
        public bool IsAnimating { get; set; }
        public double AnimationSpeed { get; set; }
        public string AnimationMode { get; set; } = "Loop";
        public string DisplayName { get; set; } = string.Empty;
        public string Group { get; set; } = "General";
        public string Unit { get; set; } = string.Empty;
        public bool IsLocked { get; set; }
    }

    public sealed class WorkspaceCurveKey
    {
        public Guid Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double InTangent { get; set; }
        public double OutTangent { get; set; }
        public double InWeight { get; set; } = 1;
        public double OutWeight { get; set; } = 1;
        public bool LinkedTangents { get; set; } = true;
        public string TangentMode { get; set; } = "Auto";
    }

    public sealed class WorkspaceEconomyNode
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "Pool";
        public string Name { get; set; } = string.Empty;
        public string Resource { get; set; } = "Resource";
        public string Notes { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double InitialAmount { get; set; }
        public double Amount { get; set; }
        public double Capacity { get; set; } = 1000;
        public Guid? SubsystemId { get; set; }
        public bool ActionQueued { get; set; }
    }

    public sealed class WorkspaceEconomyLink
    {
        public Guid Id { get; set; }
        public Guid SourceId { get; set; }
        public Guid TargetId { get; set; }
        public string RateExpression { get; set; } = "1";
        public string ConditionExpression { get; set; } = "1";
        public string Label { get; set; } = string.Empty;
        public double Efficiency { get; set; } = 1;
        public double Chance { get; set; } = 1;
        public double Interval { get; set; }
        public double Delay { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public sealed class WorkspaceEconomyParameter
    {
        public string Name { get; set; } = string.Empty;
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Value { get; set; }
    }


    public sealed class WorkspaceEconomyScenario
    {
        public string Name { get; set; } = "Scenario";
        public Dictionary<string, double> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class WorkspaceEconomyCohort
    {
        public string Name { get; set; } = "Cohort";
        public double Weight { get; set; } = 1;
        public Dictionary<string, double> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class WorkspaceEconomyTarget
    {
        public Guid NodeId { get; set; }
        public string NodeName { get; set; } = string.Empty;
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Weight { get; set; } = 1;
    }
    public sealed class WorkspaceEconomyPendingTransfer
    {
        public Guid LinkId { get; set; }
        public Guid TargetId { get; set; }
        public double DueTime { get; set; }
        public double Amount { get; set; }
    }

    public sealed class WorkspaceEconomyRandomState
    {
        public ulong State { get; set; }
        public bool HasSpareNormal { get; set; }
        public double SpareNormal { get; set; }
    }

    public sealed class WorkspaceEconomyHistoryPoint
    {
        public double Time { get; set; }
        public Dictionary<Guid, double> Amounts { get; set; } = [];
    }

    public sealed class WorkspaceCurveChannel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "Value";
        public string ColorHex { get; set; } = "#4169E1";
        public bool IsVisible { get; set; } = true;
        public List<WorkspaceCurveKey> Keys { get; set; } = [];
    }

    public sealed class WorkspaceSharedAsset
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "Function";
        public string Name { get; set; } = "Asset";
        public string ParameterName { get; set; } = "x";
        public string Formula { get; set; } = "x";
        public string Description { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public List<SharedAssetPoint> Points { get; set; } = [];
    }

    public sealed class WorkspaceImportedTable
    {
        public string Name { get; set; } = "Table";
        public string XUnit { get; set; } = string.Empty;
        public string YUnit { get; set; } = string.Empty;
        public List<SharedAssetPoint> Rows { get; set; } = [];
    }

    public sealed class WorkspaceEconomySubsystem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "Subsystem";
        public bool IsCollapsed { get; set; }
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class WorkspaceEconomyResourceStyle
    {
        public string Resource { get; set; } = "Resource";
        public string ColorHex { get; set; } = "#4169E1";
        public string Icon { get; set; } = "●";
        public string Unit { get; set; } = string.Empty;
    }

    public sealed class WorkspaceEconomyRecipeItem
    {
        public Guid NodeId { get; set; }
        public string Resource { get; set; } = "Resource";
        public double Amount { get; set; } = 1;
    }

    public sealed class WorkspaceEconomyRecipe
    {
        public Guid Id { get; set; }
        public Guid ConverterNodeId { get; set; }
        public string Name { get; set; } = "Recipe";
        public string CraftRateExpression { get; set; } = "1";
        public bool IsEnabled { get; set; } = true;
        public List<WorkspaceEconomyRecipeItem> Inputs { get; set; } = [];
        public List<WorkspaceEconomyRecipeItem> Outputs { get; set; } = [];
    }

}
