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
    }
}
