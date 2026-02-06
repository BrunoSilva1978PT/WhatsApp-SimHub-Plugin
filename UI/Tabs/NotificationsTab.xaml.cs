using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WhatsAppSimHubPlugin.Core;
using WhatsAppSimHubPlugin.UI;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class NotificationsTab : UserControl
    {
        private SoundManager _soundManager;

        public NotificationsTab()
        {
            InitializeComponent();
        }

        public void SetSoundManager(SoundManager soundManager)
        {
            _soundManager = soundManager;
        }

        // Sound controls
        public ToggleButton VipSoundEnabledCheckboxCtrl => VipSoundEnabledCheckbox;
        public ToggleButton UrgentSoundEnabledCheckboxCtrl => UrgentSoundEnabledCheckbox;
        public ToggleButton NormalSoundEnabledCheckboxCtrl => NormalSoundEnabledCheckbox;
        public ComboBox VipSoundComboBoxCtrl => VipSoundComboBox;
        public ComboBox UrgentSoundComboBoxCtrl => UrgentSoundComboBox;
        public ComboBox NormalSoundComboBoxCtrl => NormalSoundComboBox;

        // Message Display Duration
        public Slider NormalDurationSliderCtrl => NormalDurationSlider;
        public TextBlock NormalDurationValueCtrl => NormalDurationValue;
        public Slider UrgentDurationSliderCtrl => UrgentDurationSlider;
        public TextBlock UrgentDurationValueCtrl => UrgentDurationValue;

        // Queue Limits
        public Slider MaxMessagesPerContactSliderCtrl => MaxMessagesPerContactSlider;
        public TextBlock MaxMessagesPerContactValueCtrl => MaxMessagesPerContactValue;
        public Slider MaxQueueSizeSliderCtrl => MaxQueueSizeSlider;
        public TextBlock MaxQueueSizeValueCtrl => MaxQueueSizeValue;

        // VIP/Urgent Message Behavior
        public ToggleButton RemoveAfterFirstDisplayCheckboxCtrl => RemoveAfterFirstDisplayCheckbox;
        public StackPanel ReminderIntervalPanelCtrl => ReminderIntervalPanel;
        public Slider ReminderIntervalSliderCtrl => ReminderIntervalSlider;
        public TextBlock ReminderIntervalValueCtrl => ReminderIntervalValue;

        // Refresh sound dropdowns
        public void RefreshSoundList()
        {
            if (_soundManager == null) return;

            var sounds = _soundManager.GetAvailableSounds();

            var selectedVip = VipSoundComboBox.SelectedItem as string;
            var selectedUrgent = UrgentSoundComboBox.SelectedItem as string;

            var selectedNormal = NormalSoundComboBox.SelectedItem as string;

            VipSoundComboBox.ItemsSource = sounds;
            UrgentSoundComboBox.ItemsSource = sounds;
            NormalSoundComboBox.ItemsSource = sounds;

            // Restore selection (only auto-select for VIP and Urgent, not Normal)
            if (selectedVip != null && sounds.Contains(selectedVip))
                VipSoundComboBox.SelectedItem = selectedVip;
            else if (sounds.Count > 0)
                VipSoundComboBox.SelectedIndex = 0;

            if (selectedUrgent != null && sounds.Contains(selectedUrgent))
                UrgentSoundComboBox.SelectedItem = selectedUrgent;
            else if (sounds.Count > 0)
                UrgentSoundComboBox.SelectedIndex = 0;

            if (selectedNormal != null && sounds.Contains(selectedNormal))
                NormalSoundComboBox.SelectedItem = selectedNormal;
            // Don't auto-select first item for Normal - only if user explicitly chose one
        }

        // Sound button handlers
        private void VipSoundPlay_Click(object sender, RoutedEventArgs e)
        {
            var selected = VipSoundComboBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selected))
                _soundManager?.PlaySound(selected);
        }

        private void UrgentSoundPlay_Click(object sender, RoutedEventArgs e)
        {
            var selected = UrgentSoundComboBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selected))
                _soundManager?.PlaySound(selected);
        }

        private void NormalSoundPlay_Click(object sender, RoutedEventArgs e)
        {
            var selected = NormalSoundComboBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selected))
                _soundManager?.PlaySound(selected);
        }

        private void SoundImport_Click(object sender, RoutedEventArgs e)
        {
            if (_soundManager == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|*.wav;*.mp3",
                Title = "Import notification sound"
            };

            if (dialog.ShowDialog() == true)
            {
                var imported = _soundManager.ImportSound(dialog.FileName);
                if (!string.IsNullOrEmpty(imported))
                {
                    RefreshSoundList();
                    // Select the imported sound in the dropdown that triggered the import
                    var button = sender as FrameworkElement;
                    if (button?.Name == "VipSoundImport")
                        VipSoundComboBox.SelectedItem = imported;
                    else if (button?.Name == "UrgentSoundImport")
                        UrgentSoundComboBox.SelectedItem = imported;
                    else if (button?.Name == "NormalSoundImport")
                        NormalSoundComboBox.SelectedItem = imported;
                }
            }
        }

        private void VipSoundDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = VipSoundComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            var confirmed = ConfirmDialog.Show(
                $"Delete '{selected}'?",
                null,
                "Delete Sound",
                "Delete",
                "Cancel",
                true);

            if (confirmed)
            {
                _soundManager?.DeleteSound(selected);
                RefreshSoundList();
            }
        }

        private void UrgentSoundDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = UrgentSoundComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            var confirmed = ConfirmDialog.Show(
                $"Delete '{selected}'?",
                null,
                "Delete Sound",
                "Delete",
                "Cancel",
                true);

            if (confirmed)
            {
                _soundManager?.DeleteSound(selected);
                RefreshSoundList();
            }
        }

        private void NormalSoundDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = NormalSoundComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            var confirmed = ConfirmDialog.Show(
                $"Delete '{selected}'?",
                null,
                "Delete Sound",
                "Delete",
                "Cancel",
                true);

            if (confirmed)
            {
                _soundManager?.DeleteSound(selected);
                RefreshSoundList();
            }
        }

        // +/- Button handlers for Normal Duration
        private void NormalDurationPlus_Click(object sender, RoutedEventArgs e)
        {
            if (NormalDurationSlider.Value < NormalDurationSlider.Maximum)
                NormalDurationSlider.Value += 1;
        }
        private void NormalDurationMinus_Click(object sender, RoutedEventArgs e)
        {
            if (NormalDurationSlider.Value > NormalDurationSlider.Minimum)
                NormalDurationSlider.Value -= 1;
        }

        // +/- Button handlers for Urgent Duration
        private void UrgentDurationPlus_Click(object sender, RoutedEventArgs e)
        {
            if (UrgentDurationSlider.Value < UrgentDurationSlider.Maximum)
                UrgentDurationSlider.Value += 1;
        }
        private void UrgentDurationMinus_Click(object sender, RoutedEventArgs e)
        {
            if (UrgentDurationSlider.Value > UrgentDurationSlider.Minimum)
                UrgentDurationSlider.Value -= 1;
        }

        // +/- Button handlers for Max Messages Per Contact
        private void MaxMessagesPerContactPlus_Click(object sender, RoutedEventArgs e)
        {
            if (MaxMessagesPerContactSlider.Value < MaxMessagesPerContactSlider.Maximum)
                MaxMessagesPerContactSlider.Value += 1;
        }
        private void MaxMessagesPerContactMinus_Click(object sender, RoutedEventArgs e)
        {
            if (MaxMessagesPerContactSlider.Value > MaxMessagesPerContactSlider.Minimum)
                MaxMessagesPerContactSlider.Value -= 1;
        }

        // +/- Button handlers for Max Queue Size
        private void MaxQueueSizePlus_Click(object sender, RoutedEventArgs e)
        {
            if (MaxQueueSizeSlider.Value < MaxQueueSizeSlider.Maximum)
                MaxQueueSizeSlider.Value += 1;
        }
        private void MaxQueueSizeMinus_Click(object sender, RoutedEventArgs e)
        {
            if (MaxQueueSizeSlider.Value > MaxQueueSizeSlider.Minimum)
                MaxQueueSizeSlider.Value -= 1;
        }

        // +/- Button handlers for Reminder Interval
        private void ReminderIntervalPlus_Click(object sender, RoutedEventArgs e)
        {
            if (ReminderIntervalSlider.Value < ReminderIntervalSlider.Maximum)
                ReminderIntervalSlider.Value += 1;
        }
        private void ReminderIntervalMinus_Click(object sender, RoutedEventArgs e)
        {
            if (ReminderIntervalSlider.Value > ReminderIntervalSlider.Minimum)
                ReminderIntervalSlider.Value -= 1;
        }
    }
}
