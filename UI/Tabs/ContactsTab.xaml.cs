using System.Windows.Controls;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class ContactsTab : UserControl
    {
        public ContactsTab()
        {
            InitializeComponent();
        }

        // Chat Contacts
        public ComboBox ChatContactsComboBoxCtrl => ChatContactsComboBox;
        public Button RefreshChatsButtonCtrl => RefreshChatsButton;
        public Button AddFromChatsButtonCtrl => AddFromChatsButton;
        public TextBlock ChatsStatusTextCtrl => ChatsStatusText;

        // Manual Add
        public TextBox ManualNameTextBoxCtrl => ManualNameTextBox;
        public TextBox ManualNumberTextBoxCtrl => ManualNumberTextBox;
        public Button AddManualButtonCtrl => AddManualButton;

        // Contacts DataGrid
        public DataGrid ContactsDataGridCtrl => ContactsDataGrid;
    }
}
