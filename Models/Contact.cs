using System.ComponentModel;

namespace WhatsAppSimHubPlugin.Models
{
    public class Contact : INotifyPropertyChanged
    {
        private string _name;
        private string _number;
        private bool _isVip;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(DisplayText)); // Notificar DisplayText
                }
            }
        }

        public string Number
        {
            get => _number;
            set
            {
                if (_number != value)
                {
                    _number = value;
                    OnPropertyChanged(nameof(Number));
                    OnPropertyChanged(nameof(FormattedNumber)); // Notificar calculada
                    OnPropertyChanged(nameof(DisplayText)); // Notificar DisplayText
                }
            }
        }

        public bool IsVip
        {
            get => _isVip;
            set
            {
                if (_isVip != value)
                {
                    _isVip = value;
                    OnPropertyChanged(nameof(IsVip));
                }
            }
        }

        // Propriedade calculada para UI (formato: +351910203114)
        public string FormattedNumber => $"+{Number?.Replace("+", "")}";
        
        // Propriedade para ComboBox (formato: Nome (+351910203114))
        public string DisplayText => $"{Name} ({FormattedNumber})";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
