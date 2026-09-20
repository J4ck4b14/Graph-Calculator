using System;
using System.Collections.Generic;

namespace GraphCalculator
{
    public sealed class EconomyProjectFile
    {
        public const int CurrentVersion = 4;

        public int Version { get; set; } = CurrentVersion;
        public string Format { get; set; } = "GraphCalculator.Economy";
        public string AppVersion { get; set; } = string.Empty;
        public string Name { get; set; } = "Economy model";
        public string Description { get; set; } = string.Empty;
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
        public double SimulationTime { get; set; }
        public double TimeStep { get; set; } = 0.1;
        public double PlaybackSpeed { get; set; } = 1;
        public long Seed { get; set; } = 1337;
        public double PredictionHorizon { get; set; } = 30;
        public int PredictionRuns { get; set; } = 500;
        public string SelectedScenario { get; set; } = string.Empty;
        public string SelectedCohort { get; set; } = string.Empty;
        public bool PopulationMix { get; set; }
        public double HistoryWindowSeconds { get; set; } = 30;
        public double HistoryYMaximum { get; set; }
        public bool HistoryShowCapacities { get; set; }
        public bool HistorySelectionCustomized { get; set; }
        public List<Guid> HistoryVisibleNodeIds { get; set; } = [];
        public List<WorkspaceEconomyNode> Nodes { get; set; } = [];
        public List<WorkspaceEconomyLink> Links { get; set; } = [];
        public List<WorkspaceEconomyParameter> Parameters { get; set; } = [];
        public List<WorkspaceEconomyScenario> Scenarios { get; set; } = [];
        public List<WorkspaceEconomyCohort> Cohorts { get; set; } = [];
        public List<WorkspaceEconomyTarget> Targets { get; set; } = [];
        public List<WorkspaceEconomySubsystem> Subsystems { get; set; } = [];
        public List<WorkspaceEconomyRecipe> Recipes { get; set; } = [];
        public List<WorkspaceEconomyResourceStyle> ResourceStyles { get; set; } = [];

        // Version 2+ keeps enough transient state to continue a paused stochastic simulation faithfully.
        public List<WorkspaceEconomyPendingTransfer> PendingTransfers { get; set; } = [];
        public Dictionary<Guid, double> NextActivations { get; set; } = [];
        public Dictionary<Guid, WorkspaceEconomyRandomState> LinkRandomStates { get; set; } = [];
        public List<WorkspaceEconomyHistoryPoint> History { get; set; } = [];
    }
}
