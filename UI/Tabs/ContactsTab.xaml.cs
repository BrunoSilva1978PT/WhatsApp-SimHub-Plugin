using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class ContactsTab : UserControl
    {
        // Event that parent can subscribe to for Remove button clicks
        public event EventHandler<Contact> RemoveContactRequested;

        // Event for VIP checkbox changed (to save settings)
        public event Action VipCheckboxChanged;

        // Events for search/filter in ComboBoxes
        public event EventHandler<string> GoogleContactsSearchChanged;

        public ContactsTab()
        {
            InitializeComponent();

            // Wire up text changed events for search filtering
            GoogleContactsComboBox.AddHandler(
                System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(GoogleContactsComboBox_TextChanged));
        }

        private void GoogleContactsComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox == null) return;

            var text = comboBox.Text ?? string.Empty;
            GoogleContactsSearchChanged?.Invoke(this, text);
        }

        // Google Contacts
        public Button GoogleConnectButtonCtrl => GoogleConnectButton;
        public Ellipse GoogleStatusIndicatorCtrl => GoogleStatusIndicator;
        public TextBlock GoogleStatusTextCtrl => GoogleStatusText;
        public ComboBox GoogleContactsComboBoxCtrl => GoogleContactsComboBox;
        public Button GoogleRefreshButtonCtrl => GoogleRefreshButton;
        public Button GoogleAddButtonCtrl => GoogleAddButton;

        // Manual Add
        public TextBox ManualNameTextBoxCtrl => ManualNameTextBox;
        public TextBox ManualNumberTextBoxCtrl => ManualNumberTextBox;
        public Button AddManualButtonCtrl => AddManualButton;

        // Contacts DataGrid
        public DataGrid ContactsDataGridCtrl => ContactsDataGrid;

        // Called when Remove button is loaded in DataTemplate
        private void RemoveContactButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                // Remove first to prevent multiple subscriptions when rows are recycled/virtualized
                button.Click -= RemoveContactButton_Click;
                button.Click += RemoveContactButton_Click;
            }
        }

        private void RemoveContactButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Contact contact)
            {
                RemoveContactRequested?.Invoke(this, contact);
            }
        }

        // VIP checkbox changed - notify parent to save settings
        private void VipCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            VipCheckboxChanged?.Invoke();
        }
    }
}
