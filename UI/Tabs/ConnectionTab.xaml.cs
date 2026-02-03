using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class ConnectionTab : UserControl
    {
        public ConnectionTab()
        {
            InitializeComponent();
        }

        // Connection Status
        public Ellipse StatusIndicatorCtrl => StatusIndicator;
        public TextBlock StatusTextCtrl => StatusText;
        public TextBlock ConnectedNumberTextCtrl => ConnectedNumberText;
        public Button DisconnectButtonCtrl => DisconnectButton;
        public Button ReconnectButtonCtrl => ReconnectButton;
        public Button ResetSessionButtonCtrl => ResetSessionButton;

        // QR Code
        public Image QRCodeImageCtrl => QRCodeImage;
        public TextBlock QRCodeInstructionsCtrl => QRCodeInstructions;

        // Backend Mode
        public ComboBox BackendModeComboCtrl => BackendModeCombo;
        public ToggleButton DebugLoggingCheckBoxCtrl => DebugLoggingCheckBox;

        // WhatsApp-Web.js
        public ComboBox WhatsAppWebJsVersionComboCtrl => WhatsAppWebJsVersionCombo;
        public Button WhatsAppWebJsInstallButtonCtrl => WhatsAppWebJsInstallButton;
        public Button WhatsAppWebJsCheckButtonCtrl => WhatsAppWebJsCheckButton;
        public RadioButton WhatsAppWebJsOfficialRadioCtrl => WhatsAppWebJsOfficialRadio;
        public RadioButton WhatsAppWebJsManualRadioCtrl => WhatsAppWebJsManualRadio;
        public StackPanel WhatsAppWebJsManualPanelCtrl => WhatsAppWebJsManualPanel;
        public TextBox WhatsAppWebJsRepoTextBoxCtrl => WhatsAppWebJsRepoTextBox;
        public Button WhatsAppWebJsApplyRepoButtonCtrl => WhatsAppWebJsApplyRepoButton;

        // Baileys
        public ComboBox BaileysVersionComboCtrl => BaileysVersionCombo;
        public Button BaileysInstallButtonCtrl => BaileysInstallButton;
        public Button BaileysCheckButtonCtrl => BaileysCheckButton;
        public RadioButton BaileysOfficialRadioCtrl => BaileysOfficialRadio;
        public RadioButton BaileysManualRadioCtrl => BaileysManualRadio;
        public StackPanel BaileysManualPanelCtrl => BaileysManualPanel;
        public TextBox BaileysRepoTextBoxCtrl => BaileysRepoTextBox;
        public Button BaileysApplyRepoButtonCtrl => BaileysApplyRepoButton;

        // Scripts
        public TextBlock WwjsScriptVersionTextCtrl => WwjsScriptVersionText;
        public Button WwjsScriptUpdateButtonCtrl => WwjsScriptUpdateButton;
        public TextBlock BaileysScriptVersionTextCtrl => BaileysScriptVersionText;
        public Button BaileysScriptUpdateButtonCtrl => BaileysScriptUpdateButton;
        public Button ScriptsCheckButtonCtrl => ScriptsCheckButton;

        // Dependencies
        public TextBlock DependenciesStatusTextCtrl => DependenciesStatusText;
        public StackPanel DependenciesChecklistPanelCtrl => DependenciesChecklistPanel;
        public TextBlock NodeJsStatusIconCtrl => NodeJsStatusIcon;
        public TextBlock NodeJsStatusTextCtrl => NodeJsStatusText;
        public TextBlock GitStatusIconCtrl => GitStatusIcon;
        public TextBlock GitStatusTextCtrl => GitStatusText;
        public TextBlock NpmPackagesStatusIconCtrl => NpmPackagesStatusIcon;
        public TextBlock NpmPackagesStatusTextCtrl => NpmPackagesStatusText;
        public TextBlock DependenciesProgressTextCtrl => DependenciesProgressText;
    }
}
