using System.Windows.Controls;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class DisplayTab : UserControl
    {
        public DisplayTab()
        {
            InitializeComponent();
        }

        // Device selection
        public ComboBox TargetDeviceComboBoxCtrl => TargetDeviceComboBox;
        public Button RefreshDevicesButtonCtrl => RefreshDevicesButton;
        public Button TestOverlayButtonCtrl => TestOverlayButton;
        public TextBlock DeviceStatusLabelCtrl => DeviceStatusLabel;
    }
}
