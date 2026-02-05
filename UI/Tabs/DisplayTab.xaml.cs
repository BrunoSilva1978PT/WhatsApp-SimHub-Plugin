using System.Windows;
using System.Windows.Controls;

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

        public StackPanel DashboardSelectionPanelCtrl => DashboardSelectionPanel;
        public StackPanel Dashboard1PanelCtrl => Dashboard1Panel;
        public StackPanel Dashboard2PanelCtrl => Dashboard2Panel;
        public ComboBox Dashboard1ComboBoxCtrl => Dashboard1ComboBox;
        public ComboBox Dashboard2ComboBoxCtrl => Dashboard2ComboBox;
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
