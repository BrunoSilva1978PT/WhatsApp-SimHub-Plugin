using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class QuickRepliesTab : UserControl
    {
        public QuickRepliesTab()
        {
            InitializeComponent();
        }

        // Reply 1
        public ContentPresenter Reply1ControlEditorPlaceholderCtrl => Reply1ControlEditorPlaceholder;
        public TextBox Reply1TextBoxCtrl => Reply1TextBox;

        // Reply 2
        public ContentPresenter Reply2ControlEditorPlaceholderCtrl => Reply2ControlEditorPlaceholder;
        public TextBox Reply2TextBoxCtrl => Reply2TextBox;

        // Options
        public ToggleButton ShowConfirmationCheckCtrl => ShowConfirmationCheck;
    }
}
