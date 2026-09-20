using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GraphCalculator
{
    public enum EconomyNodeKind
    {
        Source,
        Event,
        Pool,
        Gate,
        Queue,
        Register,
        Converter,
        Action,
        Sink
    }

    public sealed class EconomyNode : INotifyPropertyChanged
    {
        private string _name = "Pool";
        private string _resource = "Resource";
        private double _x = 80;
        private double _y = 80;
        private double _initialAmount = 100;
        private double _amount = 100;
        private double _capacity = 1000;
        private string _notes = string.Empty;
        private Guid? _subsystemId;
        private bool _actionQueued;

        public Guid Id { get; set; } = Guid.NewGuid();
        public EconomyNodeKind Kind { get; set; } = EconomyNodeKind.Pool;

        public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(); } }
        public string Resource { get => _resource; set { string next = string.IsNullOrWhiteSpace(value) ? "Resource" : value.Trim(); if (_resource == next) return; _resource = next; OnPropertyChanged(); } }
        public string Notes { get => _notes; set { if (_notes == value) return; _notes = value ?? string.Empty; OnPropertyChanged(); } }
        public Guid? SubsystemId { get => _subsystemId; set { if (_subsystemId == value) return; _subsystemId = value; OnPropertyChanged(); } }
        public bool ActionQueued { get => _actionQueued; set { if (_actionQueued == value) return; _actionQueued = value; OnPropertyChanged(); } }
        public double X { get => _x; set { if (Math.Abs(_x - value) < 1e-9) return; _x = value; OnPropertyChanged(); } }
        public double Y { get => _y; set { if (Math.Abs(_y - value) < 1e-9) return; _y = value; OnPropertyChanged(); } }
        public double InitialAmount { get => _initialAmount; set { if (Math.Abs(_initialAmount - value) < 1e-9) return; _initialAmount = value; OnPropertyChanged(); } }
        public double Amount { get => _amount; set { if (Math.Abs(_amount - value) < 1e-9) return; _amount = value; OnPropertyChanged(); } }
        public double Capacity { get => _capacity; set { if (Math.Abs(_capacity - value) < 1e-9) return; _capacity = value; OnPropertyChanged(); } }

        public bool IsStorage => StoresAmount(Kind);
        public bool IsGenerator => Generates(Kind);

        public static bool StoresAmount(EconomyNodeKind kind) => kind is
            EconomyNodeKind.Pool or EconomyNodeKind.Gate or EconomyNodeKind.Queue or
            EconomyNodeKind.Register or EconomyNodeKind.Converter;

        public static bool Generates(EconomyNodeKind kind) => kind is EconomyNodeKind.Source or EconomyNodeKind.Event;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class EconomyLink : INotifyPropertyChanged
    {
        private string _rateExpression = "1";
        private string _conditionExpression = "1";
        private double _efficiency = 1;
        private double _chance = 1;
        private double _interval;
        private double _delay;
        private double _startTime;
        private double _endTime = double.PositiveInfinity;
        private bool _isEnabled = true;
        private string _status = string.Empty;
        private string _label = string.Empty;

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SourceId { get; set; }
        public Guid TargetId { get; set; }

        public string RateExpression { get => _rateExpression; set { if (_rateExpression == value) return; _rateExpression = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
        public string ConditionExpression { get => _conditionExpression; set { string next = string.IsNullOrWhiteSpace(value) ? "1" : value; if (_conditionExpression == next) return; _conditionExpression = next; OnPropertyChanged(); } }
        public string Label { get => _label; set { if (_label == value) return; _label = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
        public string Display => string.IsNullOrWhiteSpace(Label) ? RateExpression : $"{Label}: {RateExpression}";

        // Efficiency doubles as conversion yield. 0.5 means half of the amount taken from the source reaches the target.
        public double Efficiency { get => _efficiency; set { double v = Math.Clamp(value, 0, 100); if (Math.Abs(_efficiency-v)<1e-9) return; _efficiency = v; OnPropertyChanged(); } }
        public double Chance { get => _chance; set { double v = Math.Clamp(value, 0, 1); if (Math.Abs(_chance-v)<1e-9) return; _chance = v; OnPropertyChanged(); } }
        public double Interval { get => _interval; set { double v = Math.Max(0, value); if (Math.Abs(_interval-v)<1e-9) return; _interval = v; OnPropertyChanged(); } }
        public double Delay { get => _delay; set { double v = Math.Max(0, value); if (Math.Abs(_delay-v)<1e-9) return; _delay = v; OnPropertyChanged(); } }
        public double StartTime { get => _startTime; set { double v = Math.Max(0, value); if (Math.Abs(_startTime-v)<1e-9) return; _startTime = v; OnPropertyChanged(); } }
        public double EndTime { get => _endTime; set { double v = value <= 0 ? double.PositiveInfinity : value; if (_endTime.Equals(v)) return; _endTime = v; OnPropertyChanged(); } }
        public bool IsEnabled { get => _isEnabled; set { if (_isEnabled == value) return; _isEnabled = value; OnPropertyChanged(); } }
        public string Status { get => _status; set { if (_status == value) return; _status = value; OnPropertyChanged(); } }

        internal CalculatorEngine.CompiledExpression? CompiledRate { get; set; }
        internal CalculatorEngine.CompiledExpression? CompiledCondition { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class EconomyParameter : INotifyPropertyChanged
    {
        private double _minimum;
        private double _maximum = 10;
        private double _value = 1;
        public EconomyParameter(string name) { Name = name; }
        public string Name { get; }
        public double Minimum { get => _minimum; set { _minimum = value; if (_maximum <= _minimum) _maximum = _minimum + 1; Value = _value; OnPropertyChanged(); OnPropertyChanged(nameof(Maximum)); } }
        public double Maximum { get => _maximum; set { _maximum = value <= _minimum ? _minimum + 1 : value; Value = _value; OnPropertyChanged(); } }
        public double Value { get => _value; set { double v = Math.Clamp(value, _minimum, _maximum); if (Math.Abs(v-_value)<1e-9) return; _value=v; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class EconomyScenario : INotifyPropertyChanged
    {
        private string _name = "Scenario";
        public string Name { get => _name; set { if (_name == value) return; _name = string.IsNullOrWhiteSpace(value) ? "Scenario" : value.Trim(); OnPropertyChanged(); } }
        public Dictionary<string, double> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public override string ToString() => Name;
    }

    public sealed class EconomyCohort : INotifyPropertyChanged
    {
        private string _name = "Cohort";
        private double _weight = 1;
        public string Name { get => _name; set { if (_name == value) return; _name = string.IsNullOrWhiteSpace(value) ? "Cohort" : value.Trim(); OnPropertyChanged(); } }
        public double Weight { get => _weight; set { double next = Math.Max(0, value); if (Math.Abs(_weight - next) < 1e-9) return; _weight = next; OnPropertyChanged(); } }
        public Dictionary<string, double> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public override string ToString() => Name;
    }

    public sealed class EconomyTarget
    {
        public Guid NodeId { get; set; }
        public string NodeName { get; set; } = string.Empty;
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Weight { get; set; } = 1;
        public string Display => $"{NodeName}: {Minimum:G4} .. {Maximum:G4}";
        public override string ToString() => Display;
    }

    public sealed record EconomyHistoryPoint(double Time, IReadOnlyDictionary<Guid, double> Amounts);
    public sealed record EconomyPendingTransfer(Guid LinkId, Guid TargetId, double DueTime, double Amount);
}
