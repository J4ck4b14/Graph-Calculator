using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GraphCalculator
{
    public sealed class GraphExpression : INotifyPropertyChanged
    {
        private string _expression = string.Empty;
        private bool _isVisible = true;
        private string _statusText = string.Empty;

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

        internal CalculatorEngine.CompiledExpression? Compiled { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
