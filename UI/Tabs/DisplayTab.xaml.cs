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

        // VoCore Enable/Disable + Test button
        public ToggleButton VoCoreEnabledCheckboxCtrl => VoCoreEnabledCheckbox;
        public Button TestVoCoresButtonCtrl => TestVoCoresButton;

        // VoCore 1 controls
        public ComboBox VoCore1ComboBoxCtrl => VoCore1ComboBox;
        public Button RemoveVoCore1ButtonCtrl => RemoveVoCore1Button;

        // VoCore 2 controls
        public ComboBox VoCore2ComboBoxCtrl => VoCore2ComboBox;
        public Button RemoveVoCore2ButtonCtrl => RemoveVoCore2Button;
    }
}
