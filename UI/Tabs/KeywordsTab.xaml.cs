using System;
using System.Windows;
using System.Windows.Controls;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class KeywordsTab : UserControl
    {
        // Event that parent can subscribe to for Remove button clicks
        public event EventHandler<string> RemoveKeywordRequested;

        public KeywordsTab()
        {
            InitializeComponent();
        }

        // New keyword input
        public TextBox NewKeywordCtrl => NewKeyword;
        public Button AddKeywordButtonCtrl => AddKeywordButton;

        // Keywords list
        public ListBox KeywordsListBoxCtrl => KeywordsListBox;

        // Called when Remove button is loaded in DataTemplate
        private void RemoveKeywordButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.Click += RemoveKeywordButton_Click;
            }
        }

        private void RemoveKeywordButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string keyword)
            {
                RemoveKeywordRequested?.Invoke(this, keyword);
            }
        }
    }
}
