using System.Windows;
using System.Windows.Input;

namespace WhatsAppSimHubPlugin.UI
{
    /// <summary>
    /// Custom confirm dialog with dark theme matching the plugin UI
    /// </summary>
    public partial class ConfirmDialog : Window
    {
        public bool Result { get; private set; } = false;

        public ConfirmDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Show a confirmation dialog
        /// </summary>
        /// <param name="owner">Parent window</param>
        /// <param name="message">Main message</param>
        /// <param name="subMessage">Optional sub-message (gray text)</param>
        /// <param name="title">Dialog title</param>
        /// <param name="confirmText">Confirm button text</param>
        /// <param name="cancelText">Cancel button text</param>
        /// <param name="isDangerous">If true, confirm button will be red</param>
        /// <returns>True if confirmed, false otherwise</returns>
        public static bool Show(
            Window owner,
            string message,
            string subMessage = null,
            string title = "Confirm",
            string confirmText = "Confirm",
            string cancelText = "Cancel",
            bool isDangerous = false)
        {
            var dialog = new ConfirmDialog();
            dialog.Owner = owner;
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.ConfirmButton.Content = confirmText;
            dialog.CancelButton.Content = cancelText;

            if (!string.IsNullOrEmpty(subMessage))
            {
                dialog.SubMessageText.Text = subMessage;
                dialog.SubMessageText.Visibility = Visibility.Visible;
            }

            if (isDangerous)
            {
                // Red button for dangerous actions (delete, remove, etc.)
                dialog.ConfirmButton.Style = (Style)dialog.FindResource("DangerButtonStyle");
            }

            dialog.ShowDialog();
            return dialog.Result;
        }

        /// <summary>
        /// Show a confirmation dialog (without owner window)
        /// </summary>
        public static bool Show(
            string message,
            string subMessage = null,
            string title = "Confirm",
            string confirmText = "Confirm",
            string cancelText = "Cancel",
            bool isDangerous = false)
        {
            var dialog = new ConfirmDialog();
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.ConfirmButton.Content = confirmText;
            dialog.CancelButton.Content = cancelText;

            if (!string.IsNullOrEmpty(subMessage))
            {
                dialog.SubMessageText.Text = subMessage;
                dialog.SubMessageText.Visibility = Visibility.Visible;
            }

            if (isDangerous)
            {
                dialog.ConfirmButton.Style = (Style)dialog.FindResource("DangerButtonStyle");
            }

            dialog.ShowDialog();
            return dialog.Result;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }
    }
}
