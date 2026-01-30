using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WhatsAppSimHubPlugin.UI
{
    public partial class SetupControl : UserControl
    {
        // Event para notificar quando user clica Retry
        public event EventHandler RetryRequested;
        
        // Event para notificar quando user clica Continue
        public event EventHandler ContinueRequested;
        
        public SetupControl()
        {
            InitializeComponent();
        }
        
        public void UpdateNodeStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    NodeStatusIcon.Text = "❌";
                    NodeStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71)); // Red
                }
                else if (isComplete)
                {
                    NodeStatusIcon.Text = "✅";
                    NodeStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(14, 231, 160)); // Green
                }
                else
                {
                    NodeStatusIcon.Text = "⏳";
                    NodeStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // Blue
                }
                NodeStatusText.Text = status;
            });
        }
        
        public void UpdateGitStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    GitStatusIcon.Text = "❌";
                    GitStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    GitStatusIcon.Text = "✅";
                    GitStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(14, 231, 160));
                }
                else
                {
                    GitStatusIcon.Text = "⏳";
                    GitStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                GitStatusText.Text = status;
            });
        }
        
        // Legacy method - delegates to both library status updates
        public void UpdateNpmStatus(string status, bool isComplete, bool isError = false)
        {
            UpdateWhatsAppWebJsStatus(status, isComplete, isError);
            UpdateBaileysStatus(status, isComplete, isError);
        }

        public void UpdateWhatsAppWebJsStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    WhatsAppWebJsStatusIcon.Text = "❌";
                    WhatsAppWebJsStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    WhatsAppWebJsStatusIcon.Text = "✅";
                    WhatsAppWebJsStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(14, 231, 160));
                }
                else
                {
                    WhatsAppWebJsStatusIcon.Text = "⏳";
                    WhatsAppWebJsStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                WhatsAppWebJsStatusText.Text = status;
            });
        }

        public void UpdateBaileysStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    BaileysStatusIcon.Text = "❌";
                    BaileysStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    BaileysStatusIcon.Text = "✅";
                    BaileysStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(14, 231, 160));
                }
                else
                {
                    BaileysStatusIcon.Text = "⏳";
                    BaileysStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                BaileysStatusText.Text = status;
            });
        }
        
        public void UpdateProgress(double value, string overallStatus)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = value;
                OverallStatusText.Text = overallStatus;
            });
        }
        
        /// <summary>
        /// Mostra o botão Retry (chamado quando há erro no setup)
        /// </summary>
        public void ShowRetryButton()
        {
            Dispatcher.Invoke(() =>
            {
                RetryButton.Visibility = Visibility.Visible;
            });
        }
        
        /// <summary>
        /// Esconde o botão Retry (chamado quando reinicia setup)
        /// </summary>
        public void HideRetryButton()
        {
            Dispatcher.Invoke(() =>
            {
                RetryButton.Visibility = Visibility.Collapsed;
            });
        }
        
        /// <summary>
        /// Mostra o botão Continue (chamado quando setup completa com sucesso)
        /// </summary>
        public void ShowContinueButton()
        {
            Dispatcher.Invoke(() =>
            {
                ContinueButton.Visibility = Visibility.Visible;
            });
        }
        
        /// <summary>
        /// Event handler quando user clica no botão Retry
        /// </summary>
        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            // Esconder o botão imediatamente
            HideRetryButton();
            
            // Notificar o plugin que user quer retry
            RetryRequested?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Esconde o botão Continue (chamado quando user clica)
        /// </summary>
        public void HideContinueButton()
        {
            Dispatcher.Invoke(() =>
            {
                ContinueButton.Visibility = Visibility.Collapsed;
            });
        }
        
        /// <summary>
        /// Event handler quando user clica no botão Continue
        /// </summary>
        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            // Esconder o botão imediatamente
            HideContinueButton();
            
            // Notificar o plugin que user quer continuar
            ContinueRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
