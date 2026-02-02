using System.Windows.Controls;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class KeywordsTab : UserControl
    {
        public KeywordsTab()
        {
            InitializeComponent();
        }

        // New keyword input
        public TextBox NewKeywordCtrl => NewKeyword;
        public Button AddKeywordButtonCtrl => AddKeywordButton;

        // Keywords list
        public ListBox KeywordsListBoxCtrl => KeywordsListBox;
    }
}
