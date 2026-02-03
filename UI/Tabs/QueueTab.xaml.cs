using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class QueueTab : UserControl
    {
        public QueueTab()
        {
            InitializeComponent();
        }

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
