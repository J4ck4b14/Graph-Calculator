using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace GraphCalculator
{
    public enum GraphExpressionKind
    {
        Scalar,
        Parametric2D,
        Parametric3D,
        ParametricSurface3D,
        VectorField2D,
        Implicit2D,
        Implicit3D,
        TextureField2D
    }

    public sealed class GraphExpression : INotifyPropertyChanged
    {
        private string _expression = string.Empty;
        private bool _isVisible = true;
        private string _statusText = string.Empty;
        private string _domainMinX = string.Empty;
        private string _domainMaxX = string.Empty;
        private string _domainMinY = string.Empty;
        private string _domainMaxY = string.Empty;
        private bool _showYDomain;
        private GraphExpressionKind _kind;

        public GraphExpression(Brush color)
        {
            Color = color;
        }

        public Brush Color { get; }

        public string Expression
        {
            get => _expression;
            set
            {
                if (_expression == value) return;
                _expression = value;
                OnPropertyChanged();
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public string DomainMinX
        {
            get => _domainMinX;
            set
            {
                if (_domainMinX == value) return;
                _domainMinX = value;
                OnPropertyChanged();
            }
        }

        public string DomainMaxX
        {
            get => _domainMaxX;
            set
            {
                if (_domainMaxX == value) return;
                _domainMaxX = value;
                OnPropertyChanged();
            }
        }

        public string DomainMinY
        {
            get => _domainMinY;
            set
            {
                if (_domainMinY == value) return;
                _domainMinY = value;
                OnPropertyChanged();
            }
        }

        public string DomainMaxY
        {
            get => _domainMaxY;
            set
            {
                if (_domainMaxY == value) return;
                _domainMaxY = value;
                OnPropertyChanged();
            }
        }

        public bool ShowYDomain
        {
            get => _showYDomain;
            set
            {
                if (_showYDomain == value) return;
                _showYDomain = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DomainYVisibility));
            }
        }

        public string DomainPrimaryLabel => Kind switch
        {
            GraphExpressionKind.Scalar => "x",
            GraphExpressionKind.ParametricSurface3D => "u",
            GraphExpressionKind.VectorField2D => "x",
            GraphExpressionKind.Implicit2D => "x",
            GraphExpressionKind.Implicit3D => "x",
            GraphExpressionKind.TextureField2D => "x",
            _ => "t"
        };

        public string DomainSecondaryLabel => Kind == GraphExpressionKind.ParametricSurface3D ? "v" : "y";

        public Visibility DomainYVisibility =>
            (Kind == GraphExpressionKind.ParametricSurface3D
             || Kind == GraphExpressionKind.VectorField2D
             || Kind == GraphExpressionKind.Implicit2D
             || Kind == GraphExpressionKind.Implicit3D
             || Kind == GraphExpressionKind.TextureField2D
             || (ShowYDomain && Kind == GraphExpressionKind.Scalar))
                ? Visibility.Visible
                : Visibility.Collapsed;

        public GraphExpressionKind Kind
        {
            get => _kind;
            internal set
            {
                if (_kind == value) return;
                _kind = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DomainPrimaryLabel));
                OnPropertyChanged(nameof(DomainSecondaryLabel));
                OnPropertyChanged(nameof(DomainYVisibility));
            }
        }

        internal CalculatorEngine.CompiledExpression? Compiled { get; set; }
        internal IReadOnlyList<CalculatorEngine.CompiledExpression> Components { get; set; } = [];

        internal IEnumerable<CalculatorEngine.CompiledExpression> CompiledParts
        {
            get
            {
                if (Compiled != null) yield return Compiled;
                foreach (CalculatorEngine.CompiledExpression component in Components) yield return component;
            }
        }

        internal IReadOnlyCollection<string> Variables => CompiledParts
            .SelectMany(part => part.Variables)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
