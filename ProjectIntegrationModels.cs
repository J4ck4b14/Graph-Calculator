using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GraphCalculator
{
    public enum SharedAssetKind { Function, Curve, Table }

    public sealed class SharedAsset : INotifyPropertyChanged
    {
        private string _name = "Asset";
        private string _parameterName = "x";
        private string _formula = "x";
        private string _description = string.Empty;
        private string _unit = string.Empty;

        public Guid Id { get; set; } = Guid.NewGuid();
        public SharedAssetKind Kind { get; set; } = SharedAssetKind.Function;
        public string Name { get => _name; set { _name = string.IsNullOrWhiteSpace(value) ? "Asset" : value.Trim(); OnPropertyChanged(); } }
        public string ParameterName { get => _parameterName; set { _parameterName = string.IsNullOrWhiteSpace(value) ? "x" : value.Trim(); OnPropertyChanged(); } }
        public string Formula { get => _formula; set { _formula = string.IsNullOrWhiteSpace(value) ? "0" : value; OnPropertyChanged(); } }
        public string Description { get => _description; set { _description = value ?? string.Empty; OnPropertyChanged(); } }
        public string Unit { get => _unit; set { _unit = value ?? string.Empty; OnPropertyChanged(); } }
        public List<SharedAssetPoint> Points { get; set; } = [];
        public override string ToString() => $"{Name}({ParameterName})";
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed record SharedAssetPoint(double X, double Y);

    public sealed class CurveChannel : INotifyPropertyChanged
    {
        private string _name = "Value";
        private string _colorHex = "#4169E1";
        private bool _isVisible = true;
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get => _name; set { _name = string.IsNullOrWhiteSpace(value) ? "Value" : value.Trim(); OnPropertyChanged(); } }
        public string ColorHex { get => _colorHex; set { _colorHex = string.IsNullOrWhiteSpace(value) ? "#4169E1" : value.Trim(); OnPropertyChanged(); } }
        public bool IsVisible { get => _isVisible; set { if (_isVisible == value) return; _isVisible = value; OnPropertyChanged(); } }
        public List<CurveKey> Keys { get; set; } = [];
        public override string ToString() => Name;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class EconomySubsystem : INotifyPropertyChanged
    {
        private string _name = "Subsystem";
        private bool _isCollapsed;
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get => _name; set { _name = string.IsNullOrWhiteSpace(value) ? "Subsystem" : value.Trim(); OnPropertyChanged(); } }
        public bool IsCollapsed { get => _isCollapsed; set { if (_isCollapsed == value) return; _isCollapsed = value; OnPropertyChanged(); } }
        public string Notes { get; set; } = string.Empty;
        public override string ToString() => Name;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class EconomyResourceStyle : INotifyPropertyChanged
    {
        private string _resource = "Resource";
        private string _colorHex = "#4169E1";
        private string _icon = "●";
        private string _unit = string.Empty;
        public string Resource { get => _resource; set { _resource = string.IsNullOrWhiteSpace(value) ? "Resource" : value.Trim(); OnPropertyChanged(); } }
        public string ColorHex { get => _colorHex; set { _colorHex = string.IsNullOrWhiteSpace(value) ? "#4169E1" : value.Trim(); OnPropertyChanged(); } }
        public string Icon { get => _icon; set { _icon = string.IsNullOrWhiteSpace(value) ? "●" : value.Trim(); OnPropertyChanged(); } }
        public string Unit { get => _unit; set { _unit = value ?? string.Empty; OnPropertyChanged(); } }
        public override string ToString() => Resource;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class EconomyRecipe : INotifyPropertyChanged
    {
        private string _name = "Recipe";
        private string _craftRateExpression = "1";
        private bool _isEnabled = true;
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ConverterNodeId { get; set; }
        public string Name { get => _name; set { _name = string.IsNullOrWhiteSpace(value) ? "Recipe" : value.Trim(); OnPropertyChanged(); } }
        public string CraftRateExpression { get => _craftRateExpression; set { _craftRateExpression = string.IsNullOrWhiteSpace(value) ? "1" : value; OnPropertyChanged(); } }
        public bool IsEnabled { get => _isEnabled; set { if (_isEnabled == value) return; _isEnabled = value; OnPropertyChanged(); } }
        public List<EconomyRecipeItem> Inputs { get; set; } = [];
        public List<EconomyRecipeItem> Outputs { get; set; } = [];
        public override string ToString() => Name;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class EconomyRecipeItem
    {
        public Guid NodeId { get; set; }
        public string Resource { get; set; } = "Resource";
        public double Amount { get; set; } = 1;
        public override string ToString() => $"{Amount:G4} {Resource}";
    }

    public sealed record EconomyDebugEvent(
        double Time,
        Guid LinkId,
        string Flow,
        double Requested,
        double Accepted,
        double Delivered,
        bool ConditionPassed,
        bool ChancePassed,
        double ConditionValue,
        double ChanceRoll,
        double ChanceThreshold,
        string Reason)
    {
        public string Detail => $"requested={Requested:G6} accepted={Accepted:G6} delivered={Delivered:G6} · condition={ConditionValue:G5} · roll={(double.IsFinite(ChanceRoll) ? ChanceRoll.ToString("G5") : "—")}/{ChanceThreshold:G5} · {Reason}";
    }

    public sealed class ImportedTable
    {
        public string Name { get; set; } = "Table";
        public string XUnit { get; set; } = string.Empty;
        public string YUnit { get; set; } = string.Empty;
        public List<SharedAssetPoint> Rows { get; set; } = [];
        public override string ToString() => Name;
    }
}
