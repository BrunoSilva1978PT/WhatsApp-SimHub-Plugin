using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class DisplayTab : UserControl
    {
        public DisplayTab()
        {
            InitializeComponent();
        }

        // UI Element accessors for SettingsControl
        public TextBlock NoDevicesMessageCtrl => NoDevicesMessage;
        public Border DeviceTableBorderCtrl => DeviceTableBorder;
        public ItemsControl DeviceListContainerCtrl => DeviceListContainer;
        public ColumnDefinition Column2HeaderCtrl => Column2Header;
        public TextBlock Column2HeaderTextCtrl => Column2HeaderText;

        public Button TestVoCoresButtonCtrl => TestVoCoresButton;

        // VoCore #1 Config accessors
        public StackPanel VoCore1ConfigPanelCtrl => VoCore1ConfigPanel;
        public RadioButton Dash1_Layer1RadioCtrl => Dash1_Layer1Radio;
        public RadioButton Dash1_Layer2RadioCtrl => Dash1_Layer2Radio;
        public StackPanel Dash1_Layer2PanelCtrl => Dash1_Layer2Panel;
        public ComboBox Dash1_Layer1ComboBoxCtrl => Dash1_Layer1ComboBox;
        public ComboBox Dash1_Layer2ComboBoxCtrl => Dash1_Layer2ComboBox;
        public Button VoCore1ApplyButtonCtrl => VoCore1ApplyButton;

        // VoCore #2 Config accessors
        public StackPanel VoCore2ConfigPanelCtrl => VoCore2ConfigPanel;
        public RadioButton Dash2_Layer1RadioCtrl => Dash2_Layer1Radio;
        public RadioButton Dash2_Layer2RadioCtrl => Dash2_Layer2Radio;
        public StackPanel Dash2_Layer2PanelCtrl => Dash2_Layer2Panel;
        public ComboBox Dash2_Layer1ComboBoxCtrl => Dash2_Layer1ComboBox;
        public ComboBox Dash2_Layer2ComboBoxCtrl => Dash2_Layer2ComboBox;
        public Button VoCore2ApplyButtonCtrl => VoCore2ApplyButton;

        // Events - will be wired up from SettingsControl
        public event System.Action VoCore1ApplyEvent;
        public event System.Action VoCore2ApplyEvent;
        public event System.Action VoCore1LayerChangedEvent;
        public event System.Action VoCore2LayerChangedEvent;
        public event System.Action VoCore1Layer1SelectionChangedEvent;
        public event System.Action VoCore2Layer1SelectionChangedEvent;
        public event System.Action VoCore1Layer2SelectionChangedEvent;
        public event System.Action VoCore2Layer2SelectionChangedEvent;

        // Allow RadioButton to be deselected by clicking again (shared handler for both VoCore1 and VoCore2)
        private void Radio_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var radio = sender as RadioButton;
            if (radio != null && radio.IsChecked == true)
            {
                radio.IsChecked = false;
                e.Handled = true;
            }
        }

        // Layer count changed handlers - show/hide Layer 2 panel and notify
        private void Dash1_LayerChanged(object sender, RoutedEventArgs e)
        {
            if (Dash1_Layer2Panel != null)
            {
                Dash1_Layer2Panel.Visibility = Dash1_Layer2Radio.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            VoCore1LayerChangedEvent?.Invoke();
        }

        private void Dash2_LayerChanged(object sender, RoutedEventArgs e)
        {
            if (Dash2_Layer2Panel != null)
            {
                Dash2_Layer2Panel.Visibility = Dash2_Layer2Radio.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            VoCore2LayerChangedEvent?.Invoke();
        }

        // Apply button click handlers
        private void VoCore1Apply_Click(object sender, RoutedEventArgs e)
        {
            VoCore1ApplyEvent?.Invoke();
        }

        private void VoCore2Apply_Click(object sender, RoutedEventArgs e)
        {
            VoCore2ApplyEvent?.Invoke();
        }

        // Layer 1 selection changed handlers
        private void Dash1_Layer1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            VoCore1Layer1SelectionChangedEvent?.Invoke();
        }

        private void Dash2_Layer1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            VoCore2Layer1SelectionChangedEvent?.Invoke();
        }

        // Layer 2 selection changed handlers
        private void Dash1_Layer2_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            VoCore1Layer2SelectionChangedEvent?.Invoke();
        }

        private void Dash2_Layer2_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            VoCore2Layer2SelectionChangedEvent?.Invoke();
        }

    }

    /// <summary>
    /// View model for each device row in the table
    /// </summary>
    public class DeviceRowViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isVoCore1;
        private bool _isVoCore2;
        private bool _isOnline;

        public string Name { get; set; }
        public string Serial { get; set; }

        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                _isOnline = value;
                OnPropertyChanged(nameof(IsOnline));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(NameColor));
            }
        }

        public string DisplayName => IsOnline ? Name : $"{Name} (offline)";
        public string NameColor => IsOnline ? "#FFFFFF" : "#888888";

        public bool IsVoCore1
        {
            get => _isVoCore1;
            set
            {
                if (_isVoCore1 != value)
                {
                    _isVoCore1 = value;
                    OnPropertyChanged(nameof(IsVoCore1));
                    VoCore1Changed?.Invoke(this, value);
                }
            }
        }

        public bool IsVoCore2
        {
            get => _isVoCore2;
            set
            {
                if (_isVoCore2 != value)
                {
                    _isVoCore2 = value;
                    OnPropertyChanged(nameof(IsVoCore2));
                    VoCore2Changed?.Invoke(this, value);
                }
            }
        }

        public Visibility Column2Visibility { get; set; } = Visibility.Visible;
        public GridLength Column2Width { get; set; } = new GridLength(60);

        // Events for when selection changes
        public event System.Action<DeviceRowViewModel, bool> VoCore1Changed;
        public event System.Action<DeviceRowViewModel, bool> VoCore2Changed;

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
