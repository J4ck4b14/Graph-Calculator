using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GraphCalculator
{
    public sealed class GraphParameter : INotifyPropertyChanged
    {
        private double _minimum = -10;
        private double _maximum = 10;
        private double _value;
        private bool _isAnimating;
        private double _animationSpeed = 1;
        private string _animationMode = "Loop";
        private int _animationDirection = 1;
        private string _displayName = string.Empty;
        private string _group = "General";
        private string _unit = string.Empty;
        private bool _isLocked;

        public GraphParameter(string name, double value = double.NaN)
        {
            Name = name;
            _displayName = name;
            ConfigureUsefulDefaults(name);

            double initial = double.IsFinite(value) ? value : DefaultValueFor(name);
            _value = Math.Clamp(initial, _minimum, _maximum);
        }

        private void ConfigureUsefulDefaults(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "t":
                case "time":
                    _minimum = 0;
                    _maximum = 10;
                    break;
                case "amplitude":
                    _minimum = 0;
                    _maximum = 2;
                    break;
                case "frequency":
                    _minimum = 0;
                    _maximum = 10;
                    break;
                case "decay":
                    _minimum = 0;
                    _maximum = 5;
                    break;
                case "power":
                    _minimum = 0.1;
                    _maximum = 10;
                    break;
                case "radius":
                case "majorradius":
                    _minimum = 0;
                    _maximum = 10;
                    break;
                case "minorradius":
                    _minimum = 0.05;
                    _maximum = 4;
                    break;
                case "pitch":
                    _minimum = -2;
                    _maximum = 2;
                    break;
                case "strength":
                    _minimum = -10;
                    _maximum = 10;
                    break;
                case "sharpness":
                    _minimum = 0.1;
                    _maximum = 20;
                    break;
                case "steps":
                    _minimum = 1;
                    _maximum = 20;
                    break;
                case "seed":
                    _minimum = 0;
                    _maximum = 100;
                    break;
                case "phase":
                    _minimum = -Math.PI;
                    _maximum = Math.PI;
                    break;
                case "octaves":
                    _minimum = 1;
                    _maximum = 8;
                    break;
                case "persistence":
                    _minimum = 0;
                    _maximum = 1;
                    break;
                case "lacunarity":
                    _minimum = 1.01;
                    _maximum = 4;
                    break;
                case "difficulty":
                    _minimum = 0.25;
                    _maximum = 3;
                    break;
                case "baseincome":
                case "basespend":
                    _minimum = 0;
                    _maximum = 50;
                    break;
                case "growth":
                    _minimum = 0;
                    _maximum = 0.25;
                    break;
                case "a":
                case "b":
                    _minimum = 1;
                    _maximum = 10;
                    break;
            }
        }

        private static double DefaultValueFor(string name)
        {
            return name.ToLowerInvariant() switch
            {
                "amplitude" => 1,
                "frequency" => 1,
                "decay" => 0.3,
                "power" => 5,
                "radius" => 2,
                "majorradius" => 2,
                "minorradius" => 0.65,
                "pitch" => 0.2,
                "strength" => 1,
                "sharpness" => 3,
                "steps" => 5,
                "phase" => 0,
                "octaves" => 4,
                "persistence" => 0.5,
                "lacunarity" => 2,
                "difficulty" => 1,
                "baseincome" => 10,
                "basespend" => 8,
                "growth" => 0.03,
                "a" => 3,
                "b" => 2,
                _ => 0
            };
        }

        public string Name { get; }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? Name : value.Trim();
                if (_displayName == next) return;
                _displayName = next;
                OnPropertyChanged();
            }
        }

        public string Group
        {
            get => _group;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "General" : value.Trim();
                if (_group == next) return;
                _group = next;
                OnPropertyChanged();
            }
        }

        public string Unit
        {
            get => _unit;
            set
            {
                string next = value?.Trim() ?? string.Empty;
                if (_unit == next) return;
                _unit = next;
                OnPropertyChanged();
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value) return;
                _isLocked = value;
                if (_isLocked && _isAnimating)
                {
                    _isAnimating = false;
                    OnPropertyChanged(nameof(IsAnimating));
                }
                OnPropertyChanged();
            }
        }

        public double Minimum
        {
            get => _minimum;
            set
            {
                if (!double.IsFinite(value) || value >= _maximum || Math.Abs(_minimum - value) < 1e-12) return;
                _minimum = value;
                OnPropertyChanged();
                if (_value < _minimum) Value = _minimum;
            }
        }

        public double Maximum
        {
            get => _maximum;
            set
            {
                if (!double.IsFinite(value) || value <= _minimum || Math.Abs(_maximum - value) < 1e-12) return;
                _maximum = value;
                OnPropertyChanged();
                if (_value > _maximum) Value = _maximum;
            }
        }

        public double Value
        {
            get => _value;
            set
            {
                if (_isLocked || !double.IsFinite(value)) return;
                double clamped = Math.Clamp(value, Minimum, Maximum);
                if (Math.Abs(_value - clamped) < 1e-12) return;
                _value = clamped;
                OnPropertyChanged();
            }
        }

        public bool IsAnimating
        {
            get => _isAnimating;
            set
            {
                if (_isAnimating == value) return;
                _isAnimating = value;
                OnPropertyChanged();
            }
        }

        public double AnimationSpeed
        {
            get => _animationSpeed;
            set
            {
                if (!double.IsFinite(value) || value < 0 || Math.Abs(_animationSpeed - value) < 1e-12) return;
                _animationSpeed = value;
                OnPropertyChanged();
            }
        }

        public string AnimationMode
        {
            get => _animationMode;
            set
            {
                if (value is not ("Loop" or "Ping-pong" or "Once") || _animationMode == value) return;
                _animationMode = value;
                _animationDirection = 1;
                OnPropertyChanged();
            }
        }

        public void ResetAnimation()
        {
            _animationDirection = 1;
            Value = Minimum;
        }

        public bool AdvanceAnimation(double deltaSeconds)
        {
            if (IsLocked || !IsAnimating || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0 || AnimationSpeed <= 0)
            {
                return false;
            }

            double span = Maximum - Minimum;
            if (!double.IsFinite(span) || span <= 0)
            {
                return false;
            }

            double previous = Value;
            double travel = AnimationSpeed * deltaSeconds;

            switch (AnimationMode)
            {
                case "Ping-pong":
                {
                    double offset = Math.Clamp(Value - Minimum, 0, span);
                    double phase = _animationDirection >= 0
                        ? offset
                        : (2 * span) - offset;

                    phase = (phase + travel) % (2 * span);

                    if (phase <= span)
                    {
                        _animationDirection = 1;
                        Value = Minimum + phase;
                    }
                    else
                    {
                        _animationDirection = -1;
                        Value = Maximum - (phase - span);
                    }

                    break;
                }

                case "Once":
                {
                    double next = Value + travel;
                    if (next >= Maximum)
                    {
                        Value = Maximum;
                        IsAnimating = false;
                    }
                    else
                    {
                        Value = next;
                    }

                    break;
                }

                default:
                {
                    double offset = (Value - Minimum + travel) % span;
                    if (offset < 0) offset += span;
                    Value = Minimum + offset;
                    break;
                }
            }

            return Math.Abs(Value - previous) > 1e-12;
        }


        internal void SetValueIgnoringLock(double value)
        {
            if (!double.IsFinite(value)) return;
            double clamped = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(_value - clamped) < 1e-12) return;
            _value = clamped;
            OnPropertyChanged(nameof(Value));
        }

        internal void SetRangeAndValue(double minimum, double maximum, double value)
        {
            if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum) return;

            _minimum = minimum;
            _maximum = maximum;
            _value = Math.Clamp(double.IsFinite(value) ? value : minimum, minimum, maximum);
            OnPropertyChanged(nameof(Minimum));
            OnPropertyChanged(nameof(Maximum));
            OnPropertyChanged(nameof(Value));
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
