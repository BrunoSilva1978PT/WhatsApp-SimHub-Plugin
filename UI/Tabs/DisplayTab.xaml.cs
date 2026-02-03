using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class DisplayTab : UserControl
    {
        public DisplayTab()
        {
            InitializeComponent();
        }

        // VoCore Enable/Disable
        public ToggleButton VoCoreEnabledCheckboxCtrl => VoCoreEnabledCheckbox;

        // Device selection
        public ComboBox TargetDeviceComboBoxCtrl => TargetDeviceComboBox;
        public Button RefreshDevicesButtonCtrl => RefreshDevicesButton;
        public Button TestOverlayButtonCtrl => TestOverlayButton;
        public TextBlock DeviceStatusLabelCtrl => DeviceStatusLabel;
    }
}
