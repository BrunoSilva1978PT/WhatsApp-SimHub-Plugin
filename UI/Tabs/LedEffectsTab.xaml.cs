using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WhatsAppSimHubPlugin.Core;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.UI.Tabs
{
    public partial class LedEffectsTab : UserControl
    {
        private ObservableCollection<LedDeviceViewModel> _ledDevices = new ObservableCollection<LedDeviceViewModel>();
        private ObservableCollection<LedDeviceViewModel> _matrixDevices = new ObservableCollection<LedDeviceViewModel>();
        private ObservableCollection<LedDeviceViewModel> _arduinoDevices = new ObservableCollection<LedDeviceViewModel>();
        private ObservableCollection<HueDeviceViewModel> _hueDevices = new ObservableCollection<HueDeviceViewModel>();

        // Events for SettingsControl to wire up
        public event Action OnSettingsChanged;
        public event Action<string, string> OnTestLedEffect; // deviceId, priority

        public LedEffectsTab()
        {
            InitializeComponent();

            LedDevicesContainer.ItemsSource = _ledDevices;
            MatrixDevicesContainer.ItemsSource = _matrixDevices;
            ArduinoDevicesContainer.ItemsSource = _arduinoDevices;
            HueDevicesContainer.ItemsSource = _hueDevices;
        }

        // Public accessors for SettingsControl
        public CheckBox LedEffectsEnabledCheckboxCtrl => LedEffectsEnabledCheckbox;
        public CheckBox LedNormalCheckboxCtrl => LedNormalCheckbox;
        public CheckBox LedVipCheckboxCtrl => LedVipCheckbox;
        public CheckBox LedUrgentCheckboxCtrl => LedUrgentCheckbox;

        /// <summary>
        /// Populates the device lists from discovered devices and saved configs.
        /// </summary>
        public void PopulateDevices(List<DiscoveredLedDevice> discovered, List<LedDeviceConfig> savedConfigs)
        {
            _ledDevices.Clear();
            _matrixDevices.Clear();
            _arduinoDevices.Clear();
            _hueDevices.Clear();

            if (discovered == null)
            {
                UpdateEmptyTexts();
                return;
            }

            foreach (var device in discovered)
            {
                // Find saved config for this device
                var saved = savedConfigs?.FirstOrDefault(c => c.DeviceId == device.DeviceId);

                switch (device.DeviceType)
                {
                    case LedDeviceType.LedDevice:
                        var ledVm = new LedDeviceViewModel(device, saved);
                        ledVm.PropertyChanged += DeviceViewModel_PropertyChanged;
                        _ledDevices.Add(ledVm);
                        break;

                    case LedDeviceType.DeviceMatrix:
                        var matrixVm = new LedDeviceViewModel(device, saved);
                        matrixVm.PropertyChanged += DeviceViewModel_PropertyChanged;
                        _matrixDevices.Add(matrixVm);
                        break;

                    case LedDeviceType.ArduinoLeds:
                    case LedDeviceType.ArduinoMatrix:
                        var arduinoVm = new LedDeviceViewModel(device, saved);
                        arduinoVm.PropertyChanged += DeviceViewModel_PropertyChanged;
                        _arduinoDevices.Add(arduinoVm);
                        break;

                    case LedDeviceType.PhilipsHue:
                        var hueVm = new HueDeviceViewModel(device, saved);
                        hueVm.PropertyChanged += DeviceViewModel_PropertyChanged;
                        _hueDevices.Add(hueVm);
                        break;
                }
            }

            UpdateEmptyTexts();
        }

        private void UpdateEmptyTexts()
        {
            NoLedDevicesText.Visibility = _ledDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoMatrixDevicesText.Visibility = _matrixDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoArduinoDevicesText.Visibility = _arduinoDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoHueDevicesText.Visibility = _hueDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Collects all device configs from the current UI state.
        /// </summary>
        public List<LedDeviceConfig> GetAllDeviceConfigs()
        {
            var configs = new List<LedDeviceConfig>();

            foreach (var vm in _ledDevices)
                configs.Add(vm.ToConfig());
            foreach (var vm in _matrixDevices)
                configs.Add(vm.ToConfig());
            foreach (var vm in _arduinoDevices)
                configs.Add(vm.ToConfig());
            foreach (var vm in _hueDevices)
                configs.Add(vm.ToConfig());

            return configs;
        }

        private void DeviceViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnSettingsChanged?.Invoke();
        }

        // Toggle expand/collapse
        private void DeviceHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border?.DataContext is LedDeviceViewModel ledVm)
                ledVm.IsExpanded = !ledVm.IsExpanded;
            else if (border?.DataContext is HueDeviceViewModel hueVm)
                hueVm.IsExpanded = !hueVm.IsExpanded;
        }

        // Test button handlers
        private void TestLedDevice_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(deviceId))
                OnTestLedEffect?.Invoke(deviceId, "normal");
        }

        private void TestMatrixDevice_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(deviceId))
                OnTestLedEffect?.Invoke(deviceId, "normal");
        }

        private void TestArduinoDevice_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(deviceId))
                OnTestLedEffect?.Invoke(deviceId, "normal");
        }

        private void TestHueNormal_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(deviceId))
                OnTestLedEffect?.Invoke(deviceId, "normal");
        }

        private void TestHueVip_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(deviceId))
                OnTestLedEffect?.Invoke(deviceId, "vip");
        }

        private void TestHueUrgent_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = (sender as FrameworkElement)?.Tag as string;
            if (!string.IsNullOrEmpty(deviceId))
                OnTestLedEffect?.Invoke(deviceId, "urgent");
        }

        /// <summary>
        /// Finds a device config by ID across all lists.
        /// </summary>
        public LedDeviceConfig FindDeviceConfig(string deviceId)
        {
            foreach (var vm in _ledDevices)
                if (vm.DeviceId == deviceId) return vm.ToConfig();
            foreach (var vm in _matrixDevices)
                if (vm.DeviceId == deviceId) return vm.ToConfig();
            foreach (var vm in _arduinoDevices)
                if (vm.DeviceId == deviceId) return vm.ToConfig();
            foreach (var vm in _hueDevices)
                if (vm.DeviceId == deviceId) return vm.ToConfig();
            return null;
        }
    }

    #region ViewModels

    /// <summary>
    /// ViewModel for LED and Matrix device cards.
    /// </summary>
    public class LedDeviceViewModel : INotifyPropertyChanged
    {
        private bool _enabled;
        private bool _isExpanded;
        private System.Windows.Media.Color _normalColor;
        private System.Windows.Media.Color _vipColor;
        private System.Windows.Media.Color _urgentColor;
        private bool _isFlashAll = true;

        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public LedDeviceType DeviceType { get; set; }
        public int LedCount { get; set; }
        public int MatrixRows { get; set; }
        public int MatrixColumns { get; set; }

        public string LedCountText => DeviceType == LedDeviceType.ArduinoMatrix || DeviceType == LedDeviceType.DeviceMatrix
            ? $"{MatrixRows}x{MatrixColumns}"
            : $"{LedCount} LEDs";

        public string MatrixSizeText => $"{MatrixRows}x{MatrixColumns}";

        public bool IsMatrixDevice => DeviceType == LedDeviceType.ArduinoMatrix || DeviceType == LedDeviceType.DeviceMatrix;

        public string ExpandArrow => _isExpanded ? "\u25BC" : "\u25B6";

        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); OnPropertyChanged(nameof(ExpandArrow)); } }
        }

        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(nameof(Enabled)); } }
        }

        public System.Windows.Media.Color NormalColorValue
        {
            get => _normalColor;
            set { if (_normalColor != value) { _normalColor = value; OnPropertyChanged(nameof(NormalColorValue)); } }
        }

        public System.Windows.Media.Color VipColorValue
        {
            get => _vipColor;
            set { if (_vipColor != value) { _vipColor = value; OnPropertyChanged(nameof(VipColorValue)); } }
        }

        public System.Windows.Media.Color UrgentColorValue
        {
            get => _urgentColor;
            set { if (_urgentColor != value) { _urgentColor = value; OnPropertyChanged(nameof(UrgentColorValue)); } }
        }

        public bool IsFlashAll
        {
            get => _isFlashAll;
            set { if (_isFlashAll != value) { _isFlashAll = value; OnPropertyChanged(nameof(IsFlashAll)); OnPropertyChanged(nameof(IsEnvelopeIcon)); } }
        }

        public bool IsEnvelopeIcon
        {
            get => !_isFlashAll;
            set { IsFlashAll = !value; }
        }

        public LedDeviceViewModel(DiscoveredLedDevice discovered, LedDeviceConfig saved)
        {
            DeviceId = discovered.DeviceId;
            DeviceName = discovered.DeviceName;
            DeviceType = discovered.DeviceType;
            LedCount = discovered.LedCount;
            MatrixRows = discovered.MatrixRows;
            MatrixColumns = discovered.MatrixColumns;

            if (saved != null)
            {
                _enabled = saved.Enabled;
                _normalColor = HexToColor(saved.NormalColor);
                _vipColor = HexToColor(saved.VipColor);
                _urgentColor = HexToColor(saved.UrgentColor);
                _isFlashAll = saved.MatrixMode == MatrixDisplayMode.FlashAll;
            }
            else
            {
                _normalColor = HexToColor("#00FF00");
                _vipColor = HexToColor("#FF9800");
                _urgentColor = HexToColor("#FF0000");
            }
        }

        public LedDeviceConfig ToConfig()
        {
            return new LedDeviceConfig
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                DeviceType = DeviceType,
                Enabled = Enabled,
                LedCount = LedCount,
                MatrixRows = MatrixRows,
                MatrixColumns = MatrixColumns,
                NormalColor = ColorToHex(NormalColorValue),
                VipColor = ColorToHex(VipColorValue),
                UrgentColor = ColorToHex(UrgentColorValue),
                MatrixMode = IsFlashAll ? MatrixDisplayMode.FlashAll : MatrixDisplayMode.EnvelopeIcon
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        internal static System.Windows.Media.Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 7) return System.Windows.Media.Colors.White;
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return System.Windows.Media.Color.FromRgb(r, g, b);
                }
            }
            catch { }
            return System.Windows.Media.Colors.White;
        }

        internal static string ColorToHex(System.Windows.Media.Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    /// <summary>
    /// ViewModel for Philips Hue device cards.
    /// </summary>
    public class HueDeviceViewModel : INotifyPropertyChanged
    {
        private bool _enabled;
        private bool _isExpanded;
        private System.Windows.Media.Color _normalColor;
        private System.Windows.Media.Color _vipColor;
        private System.Windows.Media.Color _urgentColor;
        private System.Windows.Media.Color _hueColor2Normal;
        private System.Windows.Media.Color _hueColor2Vip;
        private System.Windows.Media.Color _hueColor2Urgent;
        private int _hueNormalEffectIndex;
        private int _hueVipEffectIndex;
        private int _hueUrgentEffectIndex;

        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public int LedCount { get; set; }
        public string LedCountText => $"{LedCount} lights";

        public ObservableCollection<HueLightViewModel> Lights { get; set; } = new ObservableCollection<HueLightViewModel>();

        public string ExpandArrow => _isExpanded ? "\u25BC" : "\u25B6";

        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); OnPropertyChanged(nameof(ExpandArrow)); } }
        }

        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(nameof(Enabled)); } }
        }

        public System.Windows.Media.Color NormalColorValue
        {
            get => _normalColor;
            set { if (_normalColor != value) { _normalColor = value; OnPropertyChanged(nameof(NormalColorValue)); } }
        }

        public System.Windows.Media.Color VipColorValue
        {
            get => _vipColor;
            set { if (_vipColor != value) { _vipColor = value; OnPropertyChanged(nameof(VipColorValue)); } }
        }

        public System.Windows.Media.Color UrgentColorValue
        {
            get => _urgentColor;
            set { if (_urgentColor != value) { _urgentColor = value; OnPropertyChanged(nameof(UrgentColorValue)); } }
        }

        public System.Windows.Media.Color HueColor2NormalValue
        {
            get => _hueColor2Normal;
            set { if (_hueColor2Normal != value) { _hueColor2Normal = value; OnPropertyChanged(nameof(HueColor2NormalValue)); } }
        }

        public System.Windows.Media.Color HueColor2VipValue
        {
            get => _hueColor2Vip;
            set { if (_hueColor2Vip != value) { _hueColor2Vip = value; OnPropertyChanged(nameof(HueColor2VipValue)); } }
        }

        public System.Windows.Media.Color HueColor2UrgentValue
        {
            get => _hueColor2Urgent;
            set { if (_hueColor2Urgent != value) { _hueColor2Urgent = value; OnPropertyChanged(nameof(HueColor2UrgentValue)); } }
        }

        public int HueNormalEffectIndex
        {
            get => _hueNormalEffectIndex;
            set { if (_hueNormalEffectIndex != value) { _hueNormalEffectIndex = value; OnPropertyChanged(nameof(HueNormalEffectIndex)); OnPropertyChanged(nameof(IsNormalAlternating)); } }
        }

        public int HueVipEffectIndex
        {
            get => _hueVipEffectIndex;
            set { if (_hueVipEffectIndex != value) { _hueVipEffectIndex = value; OnPropertyChanged(nameof(HueVipEffectIndex)); OnPropertyChanged(nameof(IsVipAlternating)); } }
        }

        public int HueUrgentEffectIndex
        {
            get => _hueUrgentEffectIndex;
            set { if (_hueUrgentEffectIndex != value) { _hueUrgentEffectIndex = value; OnPropertyChanged(nameof(HueUrgentEffectIndex)); OnPropertyChanged(nameof(IsUrgentAlternating)); } }
        }

        public bool IsNormalAlternating => _hueNormalEffectIndex == 1;
        public bool IsVipAlternating => _hueVipEffectIndex == 1;
        public bool IsUrgentAlternating => _hueUrgentEffectIndex == 1;

        public HueDeviceViewModel(DiscoveredLedDevice discovered, LedDeviceConfig saved)
        {
            DeviceId = discovered.DeviceId;
            DeviceName = discovered.DeviceName;
            LedCount = discovered.LedCount;

            // Build light list
            for (int i = 0; i < discovered.HueLightNames.Count; i++)
            {
                bool isSelected = saved?.SelectedLights?.Contains(i) ?? true; // Default all selected
                var light = new HueLightViewModel { Index = i, Name = discovered.HueLightNames[i], IsSelected = isSelected };
                light.PropertyChanged += (s, e) => OnPropertyChanged("LightsChanged");
                Lights.Add(light);
            }

            if (saved != null)
            {
                _enabled = saved.Enabled;
                _normalColor = LedDeviceViewModel.HexToColor(saved.NormalColor);
                _vipColor = LedDeviceViewModel.HexToColor(saved.VipColor);
                _urgentColor = LedDeviceViewModel.HexToColor(saved.UrgentColor);
                _hueColor2Normal = LedDeviceViewModel.HexToColor(saved.HueColor2Normal);
                _hueColor2Vip = LedDeviceViewModel.HexToColor(saved.HueColor2Vip);
                _hueColor2Urgent = LedDeviceViewModel.HexToColor(saved.HueColor2Urgent);
                _hueNormalEffectIndex = saved.HueNormalEffect == HueEffectType.Alternating ? 1 : 0;
                _hueVipEffectIndex = saved.HueVipEffect == HueEffectType.Alternating ? 1 : 0;
                _hueUrgentEffectIndex = saved.HueUrgentEffect == HueEffectType.Alternating ? 1 : 0;
            }
            else
            {
                _normalColor = LedDeviceViewModel.HexToColor("#00FF00");
                _vipColor = LedDeviceViewModel.HexToColor("#FF9800");
                _urgentColor = LedDeviceViewModel.HexToColor("#FF0000");
                _hueColor2Normal = LedDeviceViewModel.HexToColor("#FFFFFF");
                _hueColor2Vip = LedDeviceViewModel.HexToColor("#FFFFFF");
                _hueColor2Urgent = LedDeviceViewModel.HexToColor("#0000FF");
                _hueVipEffectIndex = 1; // Alternating by default for VIP
                _hueUrgentEffectIndex = 1; // Alternating by default for Urgent
            }
        }

        public LedDeviceConfig ToConfig()
        {
            return new LedDeviceConfig
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                DeviceType = LedDeviceType.PhilipsHue,
                Enabled = Enabled,
                LedCount = LedCount,
                NormalColor = LedDeviceViewModel.ColorToHex(NormalColorValue),
                VipColor = LedDeviceViewModel.ColorToHex(VipColorValue),
                UrgentColor = LedDeviceViewModel.ColorToHex(UrgentColorValue),
                HueColor2Normal = LedDeviceViewModel.ColorToHex(HueColor2NormalValue),
                HueColor2Vip = LedDeviceViewModel.ColorToHex(HueColor2VipValue),
                HueColor2Urgent = LedDeviceViewModel.ColorToHex(HueColor2UrgentValue),
                HueNormalEffect = HueNormalEffectIndex == 1 ? HueEffectType.Alternating : HueEffectType.Flash,
                HueVipEffect = HueVipEffectIndex == 1 ? HueEffectType.Alternating : HueEffectType.Flash,
                HueUrgentEffect = HueUrgentEffectIndex == 1 ? HueEffectType.Alternating : HueEffectType.Flash,
                SelectedLights = Lights.Where(l => l.IsSelected).Select(l => l.Index).ToList()
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// ViewModel for individual Hue lights within a group.
    /// </summary>
    public class HueLightViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int Index { get; set; }
        public string Name { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion
}
