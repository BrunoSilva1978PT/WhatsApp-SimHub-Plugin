using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SimHub.Plugins.OutputPlugins.GraphicalDash;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using WhatsAppSimHubPlugin.Models;
using WhatsAppSimHubPlugin.UI.Tabs;

namespace WhatsAppSimHubPlugin.UI
{
    /// <summary>
    /// Extension methods for Process to add async support in .NET Framework 4.8
    /// </summary>
    public static class ProcessExtensions
    {
        public static Task WaitForExitAsync(this Process process)
        {
            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(true);
            if (process.HasExited)
                tcs.TrySetResult(true);
            return tcs.Task;
        }
    }

    public partial class SettingsControl : UserControl
    {
        private readonly WhatsAppPlugin _plugin;
        private readonly PluginSettings _settings;
        private ObservableCollection<Contact> _contacts;
        private ObservableCollection<string> _keywords;

        // Public properties for external access (used by WhatsAppPlugin.cs)
        public Button ReconnectButton => ConnectionTab.ReconnectButtonCtrl;
        public Button DisconnectButton => ConnectionTab.DisconnectButtonCtrl;
        private bool _isLoadingDevices = false; // Flag to avoid trigger during loading
        private HashSet<string> _knownDeviceIds = new HashSet<string>(); // Known devices

        public SettingsControl(WhatsAppPlugin plugin)
        {
            InitializeComponent();

            _plugin = plugin;
            _settings = plugin.Settings;

            // ✅ IMPORTANTE: DataContext para bindings funcionarem
            this.DataContext = _settings;

            // ✅ Wire up ConnectionTab event handlers
            WireUpConnectionTabEvents();

            // ✅ Wire up ContactsTab event handlers
            WireUpContactsTabEvents();

            // ✅ Wire up KeywordsTab event handlers
            WireUpKeywordsTabEvents();

            // ✅ Wire up DisplayTab event handlers
            WireUpDisplayTabEvents();

            // ✅ Wire up QueueTab event handlers
            WireUpQueueTabEvents();

            // ✅ Wire up QuickRepliesTab event handlers
            WireUpQuickRepliesTabEvents();

            // ✅ Create ControlsEditor dynamically via reflection
            CreateControlsEditors();

            InitializeData();

            // Load available devices BEFORE loading settings (so ComboBox is populated)
            RefreshDeviceList();

            LoadSettings();

            // Device refresh is now manual via "Refresh" button (no auto-refresh timer)

            // Wire up plugin update button
            UpdateButton.Click += UpdateButton_Click;

            // Set current version text
            CurrentVersionText.Text = WhatsAppPlugin.PLUGIN_VERSION;

            // Check for plugin updates on startup
            _ = _plugin.CheckForPluginUpdateAsync();

            // Note: Connection monitoring and auto-reconnect is handled by WhatsAppPlugin
        }

        /// <summary>
        /// Wire up event handlers for ConnectionTab UserControl
        /// </summary>
        private void WireUpConnectionTabEvents()
        {
            // Connection buttons
            ConnectionTab.DisconnectButtonCtrl.Click += DisconnectButton_Click;
            ConnectionTab.ReconnectButtonCtrl.Click += ReconnectButton_Click;
            ConnectionTab.ResetSessionButtonCtrl.Click += ResetSessionButton_Click;

            // Backend mode
            ConnectionTab.BackendModeComboCtrl.SelectionChanged += BackendModeCombo_SelectionChanged;
            ConnectionTab.DebugLoggingCheckBoxCtrl.Checked += DebugLoggingCheckBox_Changed;
            ConnectionTab.DebugLoggingCheckBoxCtrl.Unchecked += DebugLoggingCheckBox_Changed;

            // WhatsApp-Web.js controls
            ConnectionTab.WhatsAppWebJsCheckButtonCtrl.Click += WhatsAppWebJsCheckButton_Click;
            ConnectionTab.WhatsAppWebJsInstallButtonCtrl.Click += WhatsAppWebJsInstallButton_Click;
            ConnectionTab.WhatsAppWebJsVersionComboCtrl.SelectionChanged += WhatsAppWebJsVersionCombo_SelectionChanged;
            ConnectionTab.WhatsAppWebJsOfficialRadioCtrl.Checked += WhatsAppWebJsSourceRadio_Changed;
            ConnectionTab.WhatsAppWebJsManualRadioCtrl.Checked += WhatsAppWebJsSourceRadio_Changed;
            ConnectionTab.WhatsAppWebJsApplyRepoButtonCtrl.Click += WhatsAppWebJsApplyRepo_Click;

            // Baileys controls
            ConnectionTab.BaileysCheckButtonCtrl.Click += BaileysCheckButton_Click;
            ConnectionTab.BaileysInstallButtonCtrl.Click += BaileysInstallButton_Click;
            ConnectionTab.BaileysVersionComboCtrl.SelectionChanged += BaileysVersionCombo_SelectionChanged;
            ConnectionTab.BaileysOfficialRadioCtrl.Checked += BaileysSourceRadio_Changed;
            ConnectionTab.BaileysManualRadioCtrl.Checked += BaileysSourceRadio_Changed;
            ConnectionTab.BaileysApplyRepoButtonCtrl.Click += BaileysApplyRepo_Click;

            // Scripts controls
            ConnectionTab.ScriptsCheckButtonCtrl.Click += ScriptsCheckButton_Click;
            ConnectionTab.WwjsScriptUpdateButtonCtrl.Click += WwjsScriptUpdateButton_Click;
            ConnectionTab.BaileysScriptUpdateButtonCtrl.Click += BaileysScriptUpdateButton_Click;
            ConnectionTab.GoogleScriptUpdateButtonCtrl.Click += GoogleScriptUpdateButton_Click;
        }

        /// <summary>
        /// Wire up event handlers for ContactsTab UserControl
        /// </summary>
        private void WireUpContactsTabEvents()
        {
            // Google Contacts
            ContactsTab.GoogleConnectButtonCtrl.Click += GoogleConnectButton_Click;
            ContactsTab.GoogleRefreshButtonCtrl.Click += GoogleRefreshButton_Click;
            ContactsTab.GoogleAddButtonCtrl.Click += GoogleAddButton_Click;
            ContactsTab.GoogleContactsSearchChanged += GoogleContactsComboBox_SearchChanged;

            // Manual add
            ContactsTab.AddManualButtonCtrl.Click += AddManualButton_Click;

            // Remove contact from DataTemplate
            ContactsTab.RemoveContactRequested += ContactsTab_RemoveContactRequested;

            // VIP checkbox changed - save settings
            ContactsTab.VipCheckboxChanged += ContactsTab_VipCheckboxChanged;
        }

        /// <summary>
        /// Wire up event handlers for KeywordsTab UserControl
        /// </summary>
        private void WireUpKeywordsTabEvents()
        {
            KeywordsTab.AddKeywordButtonCtrl.Click += AddKeyword_Click;
            KeywordsTab.NewKeywordCtrl.KeyDown += NewKeyword_KeyDown;

            // Remove keyword from DataTemplate
            KeywordsTab.RemoveKeywordRequested += KeywordsTab_RemoveKeywordRequested;
        }

        /// <summary>
        /// Wire up event handlers for DisplayTab UserControl
        /// </summary>
        private void WireUpDisplayTabEvents()
        {
            DisplayTab.TestVoCoresButtonCtrl.Click += TestVoCoresButton_Click;

            // Apply button events (for 2 layers / merge)
            DisplayTab.VoCore1ApplyEvent += OnVoCore1Apply;
            DisplayTab.VoCore2ApplyEvent += OnVoCore2Apply;

            // Layer changed events (auto-apply when switching to 1 layer)
            DisplayTab.VoCore1LayerChangedEvent += OnVoCore1LayerChanged;
            DisplayTab.VoCore2LayerChangedEvent += OnVoCore2LayerChanged;

            // Layer 1 dropdown changed events (auto-apply when in 1 layer mode)
            DisplayTab.VoCore1Layer1SelectionChangedEvent += OnVoCore1Layer1SelectionChanged;
            DisplayTab.VoCore2Layer1SelectionChangedEvent += OnVoCore2Layer1SelectionChanged;

            // Layer 2 dropdown changed events (check if merge button should be enabled)
            DisplayTab.VoCore1Layer2SelectionChangedEvent += OnVoCore1Layer2SelectionChanged;
            DisplayTab.VoCore2Layer2SelectionChangedEvent += OnVoCore2Layer2SelectionChanged;
        }

        /// <summary>
        /// Wire up event handlers for QueueTab UserControl
        /// </summary>
        private void WireUpQueueTabEvents()
        {
            QueueTab.NormalDurationSliderCtrl.ValueChanged += NormalDurationSlider_ValueChanged;
            QueueTab.UrgentDurationSliderCtrl.ValueChanged += UrgentDurationSlider_ValueChanged;
            QueueTab.MaxMessagesPerContactSliderCtrl.ValueChanged += MaxMessagesPerContactSlider_ValueChanged;
            QueueTab.MaxQueueSizeSliderCtrl.ValueChanged += MaxQueueSizeSlider_ValueChanged;
            QueueTab.RemoveAfterFirstDisplayCheckboxCtrl.Checked += RemoveAfterFirstDisplayCheckbox_Changed;
            QueueTab.RemoveAfterFirstDisplayCheckboxCtrl.Unchecked += RemoveAfterFirstDisplayCheckbox_Changed;
            QueueTab.ReminderIntervalSliderCtrl.ValueChanged += ReminderIntervalSlider_ValueChanged;
        }

        /// <summary>
        /// Wire up event handlers for QuickRepliesTab UserControl
        /// </summary>
        private void WireUpQuickRepliesTabEvents()
        {
            QuickRepliesTab.Reply1TextBoxCtrl.TextChanged += Reply1TextBox_TextChanged;
            QuickRepliesTab.Reply2TextBoxCtrl.TextChanged += Reply2TextBox_TextChanged;
        }

        private void InitializeData()
        {
            // Contacts
            _contacts = new ObservableCollection<Contact>(_settings.Contacts);
            ContactsTab.ContactsDataGridCtrl.ItemsSource = _contacts;

            // ⭐ SINCRONIZAR: quando _contacts muda, atualizar _settings.Contacts
            _contacts.CollectionChanged += (s, e) =>
            {
                _settings.Contacts.Clear();
                foreach (var contact in _contacts)
                {
                    _settings.Contacts.Add(contact);
                }
                _plugin.SaveSettings(); // Guardar automaticamente
                UpdateContactsEmptyState(); // Atualizar empty state
            };

            UpdateContactsEmptyState(); // Estado inicial

            // Keywords
            _keywords = new ObservableCollection<string>(_settings.Keywords);
            KeywordsTab.KeywordsListBoxCtrl.ItemsSource = _keywords;

            // Devices
            LoadAvailableDevices();

            // Dashboards
            LoadAvailableDashboards();
        }

        // Device row view models for the table
        private ObservableCollection<DeviceRowViewModel> _deviceRows = new ObservableCollection<DeviceRowViewModel>();

        /// <summary>
        /// Public method to refresh device list (called from DataUpdate auto-detection)
        /// </summary>
        public void RefreshDeviceList()
        {
            // Ensure we're on UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LoadAvailableDevices());
                return;
            }

            LoadAvailableDevices();
        }

        private void LoadAvailableDevices()
        {
            try
            {
                // Get connected VoCores from plugin
                var devices = _plugin.GetAvailableDevices();
                var currentDeviceIds = new HashSet<string>(devices.Select(d => d.Serial));

                // Check if there were changes
                bool hasChanges = !currentDeviceIds.SetEquals(_knownDeviceIds);

                // Also check if saved devices are not in current list (offline devices)
                if (!string.IsNullOrEmpty(_settings.VoCore1_Serial) && !currentDeviceIds.Contains(_settings.VoCore1_Serial))
                    hasChanges = true;
                if (!string.IsNullOrEmpty(_settings.VoCore2_Serial) && !currentDeviceIds.Contains(_settings.VoCore2_Serial))
                    hasChanges = true;

                bool isFirstTime = _deviceRows.Count == 0;

                if (!hasChanges && !isFirstTime)
                {
                    return;
                }

                _knownDeviceIds = currentDeviceIds;
                _isLoadingDevices = true;

                // Build list of device rows (online + offline saved devices)
                var allDevices = new List<DeviceRowViewModel>();

                // Calculate total device count to determine if column #2 should be shown
                int totalDeviceCount = devices.Count;
                if (!string.IsNullOrEmpty(_settings.VoCore1_Serial) && !devices.Any(d => d.Serial == _settings.VoCore1_Serial))
                    totalDeviceCount++;
                if (!string.IsNullOrEmpty(_settings.VoCore2_Serial) &&
                    _settings.VoCore2_Serial != _settings.VoCore1_Serial &&
                    !devices.Any(d => d.Serial == _settings.VoCore2_Serial))
                    totalDeviceCount++;

                bool showColumn2 = totalDeviceCount >= 2;
                var column2Visibility = showColumn2 ? Visibility.Visible : Visibility.Collapsed;
                var column2Width = showColumn2 ? new GridLength(60) : new GridLength(0);

                // Add online devices
                foreach (var device in devices)
                {
                    var row = new DeviceRowViewModel
                    {
                        Name = device.Name,
                        Serial = device.Serial,
                        IsOnline = true,
                        IsVoCore1 = device.Serial == _settings.VoCore1_Serial,
                        IsVoCore2 = device.Serial == _settings.VoCore2_Serial,
                        Column2Visibility = column2Visibility,
                        Column2Width = column2Width
                    };
                    row.VoCore1Changed += DeviceRow_VoCore1Changed;
                    row.VoCore2Changed += DeviceRow_VoCore2Changed;
                    allDevices.Add(row);
                }

                // Add offline saved device for VoCore1 if not in online list
                if (!string.IsNullOrEmpty(_settings.VoCore1_Serial) && !devices.Any(d => d.Serial == _settings.VoCore1_Serial))
                {
                    var row = new DeviceRowViewModel
                    {
                        Name = !string.IsNullOrEmpty(_settings.VoCore1_Name) ? _settings.VoCore1_Name : _settings.VoCore1_Serial,
                        Serial = _settings.VoCore1_Serial,
                        IsOnline = false,
                        IsVoCore1 = true,
                        IsVoCore2 = false,
                        Column2Visibility = column2Visibility,
                        Column2Width = column2Width
                    };
                    row.VoCore1Changed += DeviceRow_VoCore1Changed;
                    row.VoCore2Changed += DeviceRow_VoCore2Changed;
                    allDevices.Add(row);
                }

                // Add offline saved device for VoCore2 if not in online list and different from VoCore1
                if (!string.IsNullOrEmpty(_settings.VoCore2_Serial) &&
                    _settings.VoCore2_Serial != _settings.VoCore1_Serial &&
                    !devices.Any(d => d.Serial == _settings.VoCore2_Serial))
                {
                    var row = new DeviceRowViewModel
                    {
                        Name = !string.IsNullOrEmpty(_settings.VoCore2_Name) ? _settings.VoCore2_Name : _settings.VoCore2_Serial,
                        Serial = _settings.VoCore2_Serial,
                        IsOnline = false,
                        IsVoCore1 = false,
                        IsVoCore2 = true,
                        Column2Visibility = column2Visibility,
                        Column2Width = column2Width
                    };
                    row.VoCore1Changed += DeviceRow_VoCore1Changed;
                    row.VoCore2Changed += DeviceRow_VoCore2Changed;
                    allDevices.Add(row);
                }

                // Update UI
                _deviceRows.Clear();
                foreach (var row in allDevices)
                {
                    _deviceRows.Add(row);
                }

                DisplayTab.DeviceListContainerCtrl.ItemsSource = _deviceRows;

                // Update UI visibility based on device count
                int totalOnlineDevices = devices.Count;
                UpdateDeviceTableVisibility(totalOnlineDevices, allDevices.Count);
                UpdateTestButtonState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadAvailableDevices error: {ex.Message}");
            }
            finally
            {
                _isLoadingDevices = false;
            }
        }

        /// <summary>
        /// Load available dashboards into ComboBoxes
        /// </summary>
        private void LoadAvailableDashboards()
        {
            try
            {
                // IMPORTANT: Unsubscribe from events during load to prevent them from firing and overwriting settings
                DisplayTab.VoCore1LayerChangedEvent -= OnVoCore1LayerChanged;
                DisplayTab.VoCore2LayerChangedEvent -= OnVoCore2LayerChanged;
                DisplayTab.VoCore1Layer1SelectionChangedEvent -= OnVoCore1Layer1SelectionChanged;
                DisplayTab.VoCore2Layer1SelectionChangedEvent -= OnVoCore2Layer1SelectionChanged;
                DisplayTab.VoCore1Layer2SelectionChangedEvent -= OnVoCore1Layer2SelectionChanged;
                DisplayTab.VoCore2Layer2SelectionChangedEvent -= OnVoCore2Layer2SelectionChanged;
                var allDashboards = new List<string>();

                // Get dashboard list from SimHub API (zero I/O)
                var dashSettings = GraphicalDashPlugin.GetSettings();
                if (dashSettings?.Items != null)
                {
                    foreach (var item in dashSettings.Items)
                    {
                        string dashName = item.Code;
                        if (string.IsNullOrEmpty(dashName)) continue;
                        // Skip merged dashboards and overlay dashboards
                        if (dashName.StartsWith("WhatsApp_merged_")) continue;
                        if (item.Metadata?.IsOverlay == true) continue;
                        allDashboards.Add(dashName);
                    }
                }

                // Sort alphabetically
                allDashboards.Sort();

                // Layer 1 dropdowns: All dashboards EXCEPT our plugin dashboards (user's existing dashboards)
                // Plus add "Default" as first option (which maps to WhatsAppPluginVocoreX)
                var layer1Dashboards = allDashboards
                    .Where(d => d != "WhatsAppPluginVocore1" && d != "WhatsAppPluginVocore2")
                    .ToList();

                // VoCore #1: Both layers show "Default" + user dashboards
                PopulateLayerComboBox(DisplayTab.Dash1_Layer1ComboBoxCtrl, layer1Dashboards, _settings.VoCore1_Layer1, "WhatsAppPluginVocore1");
                PopulateLayerComboBox(DisplayTab.Dash1_Layer2ComboBoxCtrl, layer1Dashboards, _settings.VoCore1_Layer2, "WhatsAppPluginVocore1");

                // VoCore #2: Both layers show "Default" + user dashboards
                PopulateLayerComboBox(DisplayTab.Dash2_Layer1ComboBoxCtrl, layer1Dashboards, _settings.VoCore2_Layer1, "WhatsAppPluginVocore2");
                PopulateLayerComboBox(DisplayTab.Dash2_Layer2ComboBoxCtrl, layer1Dashboards, _settings.VoCore2_Layer2, "WhatsAppPluginVocore2");

                // Update original values for change detection
                string voCore1Layer1Display = string.IsNullOrEmpty(_settings.VoCore1_Layer1) || _settings.VoCore1_Layer1 == "WhatsAppPluginVocore1" ? "Default" : _settings.VoCore1_Layer1;
                string voCore1Layer2Display = string.IsNullOrEmpty(_settings.VoCore1_Layer2) || _settings.VoCore1_Layer2 == "WhatsAppPluginVocore1" ? "Default" : _settings.VoCore1_Layer2;
                DisplayTab.UpdateVoCore1OriginalValues(voCore1Layer1Display, voCore1Layer2Display);

                string voCore2Layer1Display = string.IsNullOrEmpty(_settings.VoCore2_Layer1) || _settings.VoCore2_Layer1 == "WhatsAppPluginVocore2" ? "Default" : _settings.VoCore2_Layer1;
                string voCore2Layer2Display = string.IsNullOrEmpty(_settings.VoCore2_Layer2) || _settings.VoCore2_Layer2 == "WhatsAppPluginVocore2" ? "Default" : _settings.VoCore2_Layer2;
                DisplayTab.UpdateVoCore2OriginalValues(voCore2Layer1Display, voCore2Layer2Display);

                // Set layer count radio buttons
                DisplayTab.Dash1_Layer1RadioCtrl.IsChecked = _settings.VoCore1_LayerCount == 1;
                DisplayTab.Dash1_Layer2RadioCtrl.IsChecked = _settings.VoCore1_LayerCount == 2;
                DisplayTab.Dash1_Layer2PanelCtrl.Visibility = _settings.VoCore1_LayerCount == 2 ? Visibility.Visible : Visibility.Collapsed;

                DisplayTab.Dash2_Layer1RadioCtrl.IsChecked = _settings.VoCore2_LayerCount == 1;
                DisplayTab.Dash2_Layer2RadioCtrl.IsChecked = _settings.VoCore2_LayerCount == 2;
                DisplayTab.Dash2_Layer2PanelCtrl.Visibility = _settings.VoCore2_LayerCount == 2 ? Visibility.Visible : Visibility.Collapsed;

                // Re-subscribe to events after loading is complete
                DisplayTab.VoCore1LayerChangedEvent += OnVoCore1LayerChanged;
                DisplayTab.VoCore2LayerChangedEvent += OnVoCore2LayerChanged;
                DisplayTab.VoCore1Layer1SelectionChangedEvent += OnVoCore1Layer1SelectionChanged;
                DisplayTab.VoCore2Layer1SelectionChangedEvent += OnVoCore2Layer1SelectionChanged;
                DisplayTab.VoCore1Layer2SelectionChangedEvent += OnVoCore1Layer2SelectionChanged;
                DisplayTab.VoCore2Layer2SelectionChangedEvent += OnVoCore2Layer2SelectionChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadAvailableDashboards error: {ex.Message}");

                // Make sure to re-subscribe even if there's an error
                DisplayTab.VoCore1LayerChangedEvent += OnVoCore1LayerChanged;
                DisplayTab.VoCore2LayerChangedEvent += OnVoCore2LayerChanged;
                DisplayTab.VoCore1Layer1SelectionChangedEvent += OnVoCore1Layer1SelectionChanged;
                DisplayTab.VoCore2Layer1SelectionChangedEvent += OnVoCore2Layer1SelectionChanged;
                DisplayTab.VoCore1Layer2SelectionChangedEvent += OnVoCore1Layer2SelectionChanged;
                DisplayTab.VoCore2Layer2SelectionChangedEvent += OnVoCore2Layer2SelectionChanged;
            }
        }

        /// <summary>
        /// Populate Layer ComboBox: "Default" first, then user dashboards
        /// </summary>
        private void PopulateLayerComboBox(ComboBox comboBox, List<string> userDashboards, string selectedValue, string defaultDashboardName)
        {
            comboBox.Items.Clear();

            // Add "Default" as first option (maps to WhatsAppPluginVocoreX)
            comboBox.Items.Add("Default");

            // Add all user dashboards
            foreach (var dash in userDashboards)
            {
                comboBox.Items.Add(dash);
            }

            // Select current value
            if (string.IsNullOrEmpty(selectedValue) || selectedValue == defaultDashboardName)
            {
                comboBox.SelectedItem = "Default";
            }
            else if (userDashboards.Contains(selectedValue))
            {
                comboBox.SelectedItem = selectedValue;
            }
            else
            {
                comboBox.SelectedIndex = 0; // Default
            }
        }

        /// <summary>
        /// Update device table visibility based on device count
        /// </summary>
        private void UpdateDeviceTableVisibility(int onlineCount, int totalCount)
        {
            if (totalCount == 0)
            {
                // No devices at all
                DisplayTab.NoDevicesMessageCtrl.Visibility = Visibility.Visible;
                DisplayTab.DeviceTableBorderCtrl.Visibility = Visibility.Collapsed;
                DisplayTab.VoCore1ConfigPanelCtrl.Visibility = Visibility.Collapsed;
                DisplayTab.VoCore2ConfigPanelCtrl.Visibility = Visibility.Collapsed;
            }
            else
            {
                DisplayTab.NoDevicesMessageCtrl.Visibility = Visibility.Collapsed;
                DisplayTab.DeviceTableBorderCtrl.Visibility = Visibility.Visible;

                // Show #2 column only if 2+ total devices (online + offline)
                bool showColumn2 = totalCount >= 2;
                DisplayTab.Column2HeaderCtrl.Width = showColumn2 ? new GridLength(60) : new GridLength(0);
                DisplayTab.Column2HeaderTextCtrl.Visibility = showColumn2 ? Visibility.Visible : Visibility.Collapsed;

                // Show VoCore #1 config if VoCore1 selected
                DisplayTab.VoCore1ConfigPanelCtrl.Visibility = !string.IsNullOrEmpty(_settings.VoCore1_Serial) ? Visibility.Visible : Visibility.Collapsed;

                // Show VoCore #2 config if VoCore2 selected
                DisplayTab.VoCore2ConfigPanelCtrl.Visibility = !string.IsNullOrEmpty(_settings.VoCore2_Serial) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Handle VoCore1 selection changed from RadioButton
        /// </summary>
        private void DeviceRow_VoCore1Changed(DeviceRowViewModel row, bool isSelected)
        {
            if (_isLoadingDevices) return;

            if (isSelected)
            {
                // Deselect other devices for VoCore1
                foreach (var otherRow in _deviceRows.Where(r => r != row && r.IsVoCore1))
                {
                    otherRow.IsVoCore1 = false;
                }

                // If this device was VoCore2, remove it
                if (row.IsVoCore2)
                {
                    row.IsVoCore2 = false;
                    _plugin.ClearOverlayDashboard(_settings.VoCore2_Serial);
                    _settings.VoCore2_Serial = "";
                    _settings.VoCore2_Name = "";
                    _plugin.SetVoCore2Enabled(false);
                }

                // Set as VoCore1
                _settings.VoCore1_Serial = row.Serial;
                _settings.VoCore1_Name = row.Name;
                _plugin.SetVoCore1Enabled(true);

                // Apply dashboard from saved settings
                _plugin.SaveSettings();
                _plugin.ApplyDashboardFromSettings(1);
            }
            else
            {
                // Deselecting - clear overlay dashboard first
                _plugin.ClearOverlayDashboard(_settings.VoCore1_Serial);

                // Then clear settings
                _settings.VoCore1_Serial = "";
                _settings.VoCore1_Name = "";
                _plugin.SetVoCore1Enabled(false);
                _plugin.SaveSettings();
            }

            UpdateTestButtonState();
            UpdateDeviceTableVisibility(_knownDeviceIds.Count, _deviceRows.Count);
        }

        /// <summary>
        /// Handle VoCore2 selection changed from RadioButton
        /// </summary>
        private void DeviceRow_VoCore2Changed(DeviceRowViewModel row, bool isSelected)
        {
            if (_isLoadingDevices) return;

            if (isSelected)
            {
                // Deselect other devices for VoCore2
                foreach (var otherRow in _deviceRows.Where(r => r != row && r.IsVoCore2))
                {
                    otherRow.IsVoCore2 = false;
                }

                // If this device was VoCore1, remove it
                if (row.IsVoCore1)
                {
                    row.IsVoCore1 = false;
                    _plugin.ClearOverlayDashboard(_settings.VoCore1_Serial);
                    _settings.VoCore1_Serial = "";
                    _settings.VoCore1_Name = "";
                    _plugin.SetVoCore1Enabled(false);
                }

                // Set as VoCore2
                _settings.VoCore2_Serial = row.Serial;
                _settings.VoCore2_Name = row.Name;
                _plugin.SetVoCore2Enabled(true);

                // Apply dashboard from saved settings
                _plugin.SaveSettings();
                _plugin.ApplyDashboardFromSettings(2);
            }
            else
            {
                // Deselecting - clear overlay dashboard first
                _plugin.ClearOverlayDashboard(_settings.VoCore2_Serial);

                // Then clear settings
                _settings.VoCore2_Serial = "";
                _settings.VoCore2_Name = "";
                _plugin.SetVoCore2Enabled(false);
                _plugin.SaveSettings();
            }

            UpdateTestButtonState();
            UpdateDeviceTableVisibility(_knownDeviceIds.Count, _deviceRows.Count);
        }

        private void UpdateTestButtonState()
        {
            bool hasAnyVoCore = !string.IsNullOrEmpty(_settings.VoCore1_Serial) || !string.IsNullOrEmpty(_settings.VoCore2_Serial);
            DisplayTab.TestVoCoresButtonCtrl.IsEnabled = hasAnyVoCore;
        }

        private void LoadSettings()
        {
            try
            {
                // 🔧 Carregar Backend Mode
                foreach (ComboBoxItem item in ConnectionTab.BackendModeComboCtrl.Items)
                {
                    if (item.Tag?.ToString() == _settings.BackendMode)
                    {
                        ConnectionTab.BackendModeComboCtrl.SelectedItem = item;
                        break;
                    }
                }

                // 🔧 Carregar Debug Logging state
                ConnectionTab.DebugLoggingCheckBoxCtrl.IsChecked = LoadDebugLoggingState();

                // VoCore selection is handled by LoadAvailableDevices() which runs before this
                UpdateTestButtonState();

                // Sliders - convert from ms to seconds where necessary
                QueueTab.MaxMessagesPerContactSliderCtrl.Value = _settings.MaxGroupSize;
                QueueTab.MaxQueueSizeSliderCtrl.Value = _settings.MaxQueueSize;
                QueueTab.NormalDurationSliderCtrl.Value = _settings.NormalDuration / 1000; // ms → seconds
                QueueTab.UrgentDurationSliderCtrl.Value = _settings.UrgentDuration / 1000; // ms → seconds

                // Checkbox RemoveAfterFirstDisplay
                QueueTab.RemoveAfterFirstDisplayCheckboxCtrl.IsChecked = _settings.RemoveAfterFirstDisplay;

                // ReminderInterval slider (ms → minutes)
                QueueTab.ReminderIntervalSliderCtrl.Value = _settings.ReminderInterval / 60000;

                // Mostrar/esconder painel baseado no checkbox
                QueueTab.ReminderIntervalPanelCtrl.Visibility = _settings.RemoveAfterFirstDisplay ? Visibility.Collapsed : Visibility.Visible;

                // Quick replies - apenas textos
                QuickRepliesTab.Reply1TextBoxCtrl.Text = _settings.Reply1Text;
                QuickRepliesTab.Reply2TextBoxCtrl.Text = _settings.Reply2Text;

                QuickRepliesTab.ShowConfirmationCheckCtrl.IsChecked = _settings.ShowConfirmation;
            }
            catch (Exception ex)
            {
                ShowToast($"Error loading settings: {ex.Message}", "❌", 10);
            }

            // Check initial connection status
            CheckInitialStatus();

            // Load backend library settings
            LoadBackendLibrarySettings();
        }

        private void CheckInitialStatus()
        {
            // Check if Node.js is installed on startup
            if (!_plugin.IsNodeJsInstalled())
            {
                UpdateConnectionStatus("Node.js not installed");
            }
            else
            {
                UpdateConnectionStatus("Disconnected");
            }
        }

        #region Connection Tab

        public void UpdateConnectionStatus(string status, string number = null)
        {
            Dispatcher.Invoke(() =>
            {
                ConnectionTab.StatusTextCtrl.Text = status;

                switch (status.ToLower())
                {
                    case "connected":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(14, 122, 13)); // Green

                        // Disconnect enabled, Reconnect disabled
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = true;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 1.0;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 0.5;

                        ConnectionTab.ConnectedNumberTextCtrl.Text = number != null ? $"Connected as: +{number}" : "Connected";

                        // Hide QR code when connected
                        ConnectionTab.QRCodeImageCtrl.Visibility = Visibility.Collapsed;
                        ConnectionTab.QRCodeInstructionsCtrl.Visibility = Visibility.Collapsed;
                        break;

                    case "connecting":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Disconnect should always be available
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = true;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 1.0;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 0.5;
                        ConnectionTab.ConnectedNumberTextCtrl.Text = "Connecting to WhatsApp...";
                        break;

                    case "qr":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Disconnect should always be available
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = true;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 1.0;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 0.5;
                        ConnectionTab.ConnectedNumberTextCtrl.Text = "Waiting for QR code scan...";
                        break;


                    case "node.js not installed":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Both disabled
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 0.5;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 0.5;

                        ConnectionTab.ConnectedNumberTextCtrl.Text = "Node.js is not installed. Please install Node.js from nodejs.org";
                        break;

                    case "disconnected":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(231, 72, 119)); // Red

                        // Disconnect disabled, Reconnect enabled
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 0.5;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = true;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 1.0;

                        ConnectionTab.ConnectedNumberTextCtrl.Text = "No connection";
                        break;

                    case "connection error":
                    case "error":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(231, 72, 119)); // Red

                        // Disconnect disabled, Reconnect enabled
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 0.5;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = true;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 1.0;

                        ConnectionTab.ConnectedNumberTextCtrl.Text = "Connection failed - click Reconnect to try again";
                        break;

                    default:
                        // Handle reconnecting states (e.g., "Reconnecting (1/3)...")
                        if (status.ToLower().StartsWith("reconnecting"))
                        {
                            ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                            // Both buttons available during reconnect
                            ConnectionTab.DisconnectButtonCtrl.IsEnabled = true;
                            ConnectionTab.DisconnectButtonCtrl.Opacity = 1.0;
                            ConnectionTab.ReconnectButtonCtrl.IsEnabled = false;
                            ConnectionTab.ReconnectButtonCtrl.Opacity = 0.5;

                            ConnectionTab.ConnectedNumberTextCtrl.Text = "Auto-reconnecting...";
                        }
                        break;
                }
            });
        }

        public void UpdateQRCode(string qrData)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Usar API externa para gerar QR Code (evita conflito com DLL)
                    var qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(qrData)}";

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(qrUrl);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    ConnectionTab.QRCodeImageCtrl.Source = bitmap;
                    ConnectionTab.QRCodeImageCtrl.Visibility = Visibility.Visible;
                    ConnectionTab.QRCodeInstructionsCtrl.Visibility = Visibility.Visible;
                }
                catch (Exception)
                {
                    // Se falhar, mostrar mensagem
                    ConnectionTab.QRCodeInstructionsCtrl.Text = $"Scan this QR Code with WhatsApp:\n{qrData}";
                    ConnectionTab.QRCodeInstructionsCtrl.Visibility = Visibility.Visible;
                }
            });
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plugin.DisconnectWhatsApp();
                UpdateConnectionStatus("Disconnected");
                ConnectionTab.QRCodeImageCtrl.Visibility = Visibility.Collapsed;
                ConnectionTab.QRCodeInstructionsCtrl.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShowToast($"Error disconnecting: {ex.Message}", "❌", 10);
            }
        }



        private async void TestVoCoresButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool hasVoCore1 = !string.IsNullOrEmpty(_settings.VoCore1_Serial);
                bool hasVoCore2 = !string.IsNullOrEmpty(_settings.VoCore2_Serial);

                if (!hasVoCore1 && !hasVoCore2)
                {
                    ShowToast("Please select at least one VoCore device first!", "⚠️", 5);
                    return;
                }

                // Disable button during test
                DisplayTab.TestVoCoresButtonCtrl.IsEnabled = false;

                // Show test message (uses same dashboard properties for all VoCores)
                _plugin.ShowTestMessage();

                string testingMsg = hasVoCore1 && hasVoCore2 ? "Testing both VoCores for 5s" : "Testing VoCore for 5s";
                ShowToast(testingMsg, "✅", 5);

                // Re-enable button after 5 seconds
                await Task.Delay(5000);
                DisplayTab.TestVoCoresButtonCtrl.IsEnabled = true;
            }
            catch (Exception ex)
            {
                DisplayTab.TestVoCoresButtonCtrl.IsEnabled = true; // Ensure re-enable on error
                ShowToast($"Error testing VoCores: {ex.Message}", "❌", 10);
            }
        }

        private void MaxMessagesPerContactSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QueueTab.MaxMessagesPerContactValueCtrl != null && _settings != null)
            {
                int value = (int)e.NewValue;
                QueueTab.MaxMessagesPerContactValueCtrl.Text = value.ToString();
                _settings.MaxGroupSize = value;
            }
        }

        private void MaxQueueSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QueueTab.MaxQueueSizeValueCtrl != null && _settings != null)
            {
                int value = (int)e.NewValue;
                QueueTab.MaxQueueSizeValueCtrl.Text = value.ToString();
                _settings.MaxQueueSize = value;
            }
        }

        private void NormalDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QueueTab.NormalDurationValueCtrl != null && _settings != null)
            {
                int value = (int)e.NewValue;
                QueueTab.NormalDurationValueCtrl.Text = $"{value}s";
                _settings.NormalDuration = value * 1000; // Convert to milliseconds
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void UrgentDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QueueTab.UrgentDurationValueCtrl != null && _settings != null)
            {
                int value = (int)e.NewValue;
                QueueTab.UrgentDurationValueCtrl.Text = $"{value}s";
                _settings.UrgentDuration = value * 1000; // Convert to milliseconds
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void Reply1TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Reply1Text = QuickRepliesTab.Reply1TextBoxCtrl.Text.Trim();
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void Reply2TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Reply2Text = QuickRepliesTab.Reply2TextBoxCtrl.Text.Trim();
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void RemoveAfterFirstDisplayCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings == null)
                return;

            bool isChecked = QueueTab.RemoveAfterFirstDisplayCheckboxCtrl.IsChecked == true;
            _settings.RemoveAfterFirstDisplay = isChecked;

            // 💾 SALVAR SETTINGS AUTOMATICAMENTE
            _plugin.SaveSettings();

            // ✅ Se ativou RemoveAfterFirstDisplay, limpar mensagens VIP/URGENT antigas
            if (isChecked)
            {
                _plugin.ClearVipUrgentQueue();
            }

            // Mostrar/esconder painel do ReminderInterval
            if (QueueTab.ReminderIntervalPanelCtrl != null)
            {
                QueueTab.ReminderIntervalPanelCtrl.Visibility = isChecked ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void ReminderIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (QueueTab.ReminderIntervalValueCtrl != null && _settings != null)
            {
                int value = (int)e.NewValue;
                QueueTab.ReminderIntervalValueCtrl.Text = $"{value} min";
                _settings.ReminderInterval = value * 60000; // Convert to milliseconds
                _plugin?.SaveSettings();
            }
        }

        private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateConnectionStatus("Connecting");
                await _plugin.ReconnectWhatsApp();
            }
            catch (Exception ex)
            {
                ShowToast($"Error reconnecting: {ex.Message}", "❌", 10);
            }
        }

        private void ResetSessionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Confirm with user
                var confirmed = ConfirmDialog.Show(
                    "This will delete your saved WhatsApp session and you will need to scan the QR code again.",
                    null,
                    "Reset Session",
                    "Reset",
                    "Cancel",
                    true);

                if (!confirmed)
                    return;

                ConnectionTab.ResetSessionButtonCtrl.IsEnabled = false;
                ShowToast("Resetting session...", "🔄", 3);

                // Stop current connection
                _plugin.DisconnectWhatsApp();

                // Delete auth folder for current backend only
                var pluginPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin");

                string authPath;
                if (_settings.BackendMode == "baileys")
                {
                    // For Baileys, delete entire data_baileys folder (contains auth_info and store)
                    authPath = Path.Combine(pluginPath, "data_baileys");
                }
                else
                {
                    authPath = Path.Combine(pluginPath, "data");
                }

                if (Directory.Exists(authPath))
                {
                    try
                    {
                        Directory.Delete(authPath, true);
                        _plugin.WriteLog($"Deleted auth folder: {authPath}");
                    }
                    catch (Exception ex)
                    {
                        _plugin.WriteLog($"Failed to delete {authPath}: {ex.Message}");
                    }
                }

                // Hide QR code if visible
                ConnectionTab.QRCodeImageCtrl.Visibility = Visibility.Collapsed;
                ConnectionTab.QRCodeInstructionsCtrl.Visibility = Visibility.Collapsed;

                ShowToast("Session reset! Click Connect to scan QR code.", "✅", 5);
                UpdateConnectionStatus("Disconnected");
            }
            catch (Exception ex)
            {
                ShowToast($"Error resetting session: {ex.Message}", "❌", 10);
            }
            finally
            {
                ConnectionTab.ResetSessionButtonCtrl.IsEnabled = true;
            }
        }

        #endregion

        #region Contacts Tab

        #region Google Contacts

        private ObservableCollection<Contact> _googleContacts;
        private bool _googleConnected = false;

        /// <summary>
        /// Handle Google Connect/Disconnect button click
        /// </summary>
        private void GoogleConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_googleConnected)
            {
                // Disconnect from Google
                _plugin.GoogleDisconnect();
            }
            else
            {
                // Start Google authentication
                UpdateGoogleStatus("Authenticating...", false);
                ContactsTab.GoogleConnectButtonCtrl.IsEnabled = false;
                _plugin.GoogleStartAuth();
            }
        }

        /// <summary>
        /// Handle Google Refresh button click
        /// </summary>
        private void GoogleRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_googleConnected)
            {
                ShowToast("Please connect to Google first.", "⚠️", 5);
                return;
            }

            ContactsTab.GoogleRefreshButtonCtrl.IsEnabled = false;
            UpdateGoogleStatus("Refreshing contacts...", true);
            _plugin.GoogleGetContacts();
        }

        /// <summary>
        /// Handle Google Add button click - verifies WhatsApp before adding
        /// </summary>
        private void GoogleAddButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingWhatsApp)
            {
                ShowToast("Please wait, checking WhatsApp...", "⏳", 3);
                return;
            }

            // Try to get selected item first, then try to find by text (for editable ComboBox)
            var selected = ContactsTab.GoogleContactsComboBoxCtrl.SelectedItem as Contact;

            if (selected == null)
            {
                // Try to find contact by the text in the ComboBox
                var searchText = ContactsTab.GoogleContactsComboBoxCtrl.Text?.Trim();
                if (!string.IsNullOrEmpty(searchText))
                {
                    selected = _googleContacts.FirstOrDefault(c =>
                        c.DisplayText.Equals(searchText, StringComparison.OrdinalIgnoreCase) ||
                        c.Name.Equals(searchText, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (selected == null)
            {
                ShowToast("Please select a contact from the list.", "⚠️", 5);
                return;
            }

            // Check if already exists
            var existing = _contacts.FirstOrDefault(c =>
                c.Number.Replace("+", "").Replace(" ", "").Replace("-", "") ==
                selected.Number.Replace("+", "").Replace(" ", "").Replace("-", ""));

            if (existing != null)
            {
                ShowToast($"{existing.Name} is already in your allowed contacts list.", "ℹ️", 5);
                return;
            }

            // Prepare contact to add after verification
            _pendingContactToAdd = new Contact
            {
                Name = selected.Name,
                Number = selected.Number,
                IsVip = false
            };

            // Show checking state
            _isCheckingWhatsApp = true;
            ContactsTab.GoogleAddButtonCtrl.IsEnabled = false;
            ContactsTab.GoogleAddButtonCtrl.Content = "Checking...";

            // Verify if number has WhatsApp
            _plugin.CheckWhatsAppNumber(selected.Number);
        }

        /// <summary>
        /// Update Google Contacts status from plugin
        /// </summary>
        public void UpdateGoogleStatus(string status, bool connected)
        {
            Dispatcher.Invoke(() =>
            {
                _googleConnected = connected;
                ContactsTab.GoogleStatusTextCtrl.Text = status;

                if (connected)
                {
                    ContactsTab.GoogleStatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(14, 122, 13)); // Green
                    ContactsTab.GoogleConnectButtonCtrl.Content = "🔌 Disconnect";
                    ContactsTab.GoogleContactsComboBoxCtrl.IsEnabled = true;
                    ContactsTab.GoogleRefreshButtonCtrl.IsEnabled = true;
                    ContactsTab.GoogleAddButtonCtrl.IsEnabled = true;
                }
                else
                {
                    ContactsTab.GoogleStatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(196, 43, 28)); // Red
                    ContactsTab.GoogleConnectButtonCtrl.Content = "🔗 Connect Google";
                    ContactsTab.GoogleContactsComboBoxCtrl.IsEnabled = false;
                    ContactsTab.GoogleRefreshButtonCtrl.IsEnabled = false;
                    ContactsTab.GoogleAddButtonCtrl.IsEnabled = false;
                }

                ContactsTab.GoogleConnectButtonCtrl.IsEnabled = true;
            });
        }

        /// <summary>
        /// Update Google Contacts list from plugin
        /// </summary>
        public void UpdateGoogleContactsList(ObservableCollection<Contact> contacts)
        {
            Dispatcher.Invoke(() =>
            {
                _googleContacts = contacts;
                ContactsTab.GoogleContactsComboBoxCtrl.ItemsSource = _googleContacts;
                ContactsTab.GoogleRefreshButtonCtrl.IsEnabled = true;

                if (contacts != null && contacts.Count > 0)
                {
                    UpdateGoogleStatus($"Connected - {contacts.Count} contacts", true);
                }
                else
                {
                    UpdateGoogleStatus("Connected - No contacts found", true);
                }
            });
        }

        /// <summary>
        /// Handle Google authentication error
        /// </summary>
        public void HandleGoogleError(string error)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateGoogleStatus($"Error: {error}", false);
                ShowToast($"Google error: {error}", "❌", 10);
            });
        }

        /// <summary>
        /// Request Google status from backend on load
        /// </summary>
        public void RequestGoogleStatus()
        {
            _plugin.GoogleGetStatus();
        }

        // Pending contact to add after WhatsApp verification
        private Contact _pendingContactToAdd = null;
        private bool _isCheckingWhatsApp = false;

        /// <summary>
        /// Handle WhatsApp number verification result
        /// </summary>
        public void HandleCheckWhatsAppResult(string number, bool exists, string error)
        {
            Dispatcher.Invoke(() =>
            {
                _isCheckingWhatsApp = false;

                // Re-enable buttons
                ContactsTab.GoogleAddButtonCtrl.IsEnabled = true;
                ContactsTab.GoogleAddButtonCtrl.Content = "➕ Add";
                ContactsTab.AddManualButtonCtrl.IsEnabled = true;
                ContactsTab.AddManualButtonCtrl.Content = "➕ Add";

                if (!string.IsNullOrEmpty(error))
                {
                    ShowToast($"Error checking WhatsApp: {error}", "❌", 5);
                    _pendingContactToAdd = null;
                    return;
                }

                if (!exists)
                {
                    ShowToast($"This number does not have WhatsApp", "❌", 5);
                    _pendingContactToAdd = null;
                    return;
                }

                // Number has WhatsApp - add the contact
                if (_pendingContactToAdd != null)
                {
                    _contacts.Add(_pendingContactToAdd);
                    ShowToast($"{_pendingContactToAdd.Name} added to allowed contacts!", "✅", 5);
                    _pendingContactToAdd = null;

                    // Clear selections
                    ContactsTab.GoogleContactsComboBoxCtrl.SelectedIndex = -1;
                    ContactsTab.ManualNameTextBoxCtrl.Text = "Name";
                    ContactsTab.ManualNumberTextBoxCtrl.Text = "+351...";
                }
            });
        }

        /// <summary>
        /// Handle Google Contacts search/filter text changed
        /// </summary>
        private void GoogleContactsComboBox_SearchChanged(object sender, string searchText)
        {
            if (_googleContacts == null || _googleContacts.Count == 0)
                return;

            // If search text is empty, show all contacts
            if (string.IsNullOrWhiteSpace(searchText))
            {
                ContactsTab.GoogleContactsComboBoxCtrl.ItemsSource = _googleContacts;
                return;
            }

            // Filter contacts by name or number (case-insensitive)
            var searchLower = searchText.ToLowerInvariant();
            var filtered = _googleContacts
                .Where(c =>
                    (c.Name != null && c.Name.ToLowerInvariant().Contains(searchLower)) ||
                    (c.Number != null && c.Number.Contains(searchText)))
                .ToList();

            // Update ItemsSource with filtered results
            ContactsTab.GoogleContactsComboBoxCtrl.ItemsSource = filtered;

            // Keep dropdown open while typing
            if (!ContactsTab.GoogleContactsComboBoxCtrl.IsDropDownOpen && filtered.Count > 0)
            {
                ContactsTab.GoogleContactsComboBoxCtrl.IsDropDownOpen = true;
            }
        }

        #endregion

        #endregion

        #region Keywords Tab

        private void AddKeyword_Click(object sender, RoutedEventArgs e)
        {
            var keyword = KeywordsTab.NewKeywordCtrl.Text.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(keyword))
            {
                ShowToast("Please enter a keyword", "⚠️", 5);
                return;
            }

            // Verificar duplicados (case-insensitive)
            if (_keywords.Any(k => k.ToLowerInvariant() == keyword))
            {
                ShowToast("This keyword already exists", "ℹ️", 5);
                return;
            }

            _keywords.Add(keyword);
            _settings.Keywords.Add(keyword);

            KeywordsTab.NewKeywordCtrl.Clear();
            _plugin.SaveSettings();

        }

        private void RemoveKeyword_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var keyword = button?.Tag as string;

            if (keyword == null) return;

            _keywords.Remove(keyword);
            _settings.Keywords.Remove(keyword);
            _plugin.SaveSettings();

        }

        private void KeywordsTab_RemoveKeywordRequested(object sender, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return;

            _keywords.Remove(keyword);
            _settings.Keywords.Remove(keyword);
            _plugin.SaveSettings();
        }

        private void NewKeyword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddKeyword_Click(sender, e);
            }
        }

        #endregion

        #region Display Tab - VoCore Configuration

        // ===== Helper methods to access VoCore controls by number =====
        private string GetDefaultDashboardName(int vocoreNumber) => vocoreNumber == 1 ? "WhatsAppPluginVocore1" : "WhatsAppPluginVocore2";

        private RadioButton GetLayer1Radio(int vocoreNumber) => vocoreNumber == 1 ? DisplayTab.Dash1_Layer1RadioCtrl : DisplayTab.Dash2_Layer1RadioCtrl;
        private RadioButton GetLayer2Radio(int vocoreNumber) => vocoreNumber == 1 ? DisplayTab.Dash1_Layer2RadioCtrl : DisplayTab.Dash2_Layer2RadioCtrl;
        private ComboBox GetLayer1ComboBox(int vocoreNumber) => vocoreNumber == 1 ? DisplayTab.Dash1_Layer1ComboBoxCtrl : DisplayTab.Dash2_Layer1ComboBoxCtrl;
        private ComboBox GetLayer2ComboBox(int vocoreNumber) => vocoreNumber == 1 ? DisplayTab.Dash1_Layer2ComboBoxCtrl : DisplayTab.Dash2_Layer2ComboBoxCtrl;
        private Button GetApplyButton(int vocoreNumber) => vocoreNumber == 1 ? DisplayTab.VoCore1ApplyButtonCtrl : DisplayTab.VoCore2ApplyButtonCtrl;

        private int GetLayerCount(int vocoreNumber) => vocoreNumber == 1 ? _settings.VoCore1_LayerCount : _settings.VoCore2_LayerCount;
        private void SetLayerCount(int vocoreNumber, int value) { if (vocoreNumber == 1) _settings.VoCore1_LayerCount = value; else _settings.VoCore2_LayerCount = value; }
        private void SetLayer1(int vocoreNumber, string value) { if (vocoreNumber == 1) _settings.VoCore1_Layer1 = value; else _settings.VoCore2_Layer1 = value; }
        private void SetLayer2(int vocoreNumber, string value) { if (vocoreNumber == 1) _settings.VoCore1_Layer2 = value; else _settings.VoCore2_Layer2 = value; }

        private bool HasVoCoreChanged(int vocoreNumber) => vocoreNumber == 1 ? DisplayTab.HasVoCore1Changed() : DisplayTab.HasVoCore2Changed();
        private void UpdateOriginalValues(int vocoreNumber, string layer1, string layer2)
        {
            if (vocoreNumber == 1) DisplayTab.UpdateVoCore1OriginalValues(layer1, layer2);
            else DisplayTab.UpdateVoCore2OriginalValues(layer1, layer2);
        }

        // ===== Event handler wrappers (delegate to shared implementation) =====
        private void OnVoCore1LayerChanged() => OnVoCoreLayerChanged(1);
        private void OnVoCore2LayerChanged() => OnVoCoreLayerChanged(2);
        private void OnVoCore1Layer1SelectionChanged() => OnVoCoreLayer1SelectionChanged(1);
        private void OnVoCore2Layer1SelectionChanged() => OnVoCoreLayer1SelectionChanged(2);
        private void OnVoCore1Layer2SelectionChanged() => OnVoCoreLayer2SelectionChanged(1);
        private void OnVoCore2Layer2SelectionChanged() => OnVoCoreLayer2SelectionChanged(2);
        private void OnVoCore1Apply() => OnVoCoreApply(1);
        private void OnVoCore2Apply() => OnVoCoreApply(2);

        /// <summary>
        /// Handle VoCore layer count changed - auto-apply if 1 layer
        /// </summary>
        private void OnVoCoreLayerChanged(int vocoreNumber)
        {
            if (_plugin == null || _settings == null) return;

            bool is1Layer = GetLayer1Radio(vocoreNumber).IsChecked == true;
            string defaultDash = GetDefaultDashboardName(vocoreNumber);

            if (is1Layer)
            {
                string layer1 = GetLayer1ComboBox(vocoreNumber).SelectedItem?.ToString();
                if (layer1 == "Default") layer1 = defaultDash;

                if (!string.IsNullOrEmpty(layer1))
                {
                    SetLayerCount(vocoreNumber, 1);
                    SetLayer1(vocoreNumber, layer1);
                    _plugin.SaveSettings();
                    _plugin.ApplyDashboardDirect(vocoreNumber, layer1);
                }
            }
            else
            {
                string layer1 = GetLayer1ComboBox(vocoreNumber).SelectedItem?.ToString();
                string layer2 = GetLayer2ComboBox(vocoreNumber).SelectedItem?.ToString();
                if (layer1 == "Default") layer1 = defaultDash;
                if (layer2 == "Default") layer2 = defaultDash;

                SetLayerCount(vocoreNumber, 2);
                _plugin.SaveSettings();

                if (!string.IsNullOrEmpty(layer1) && !string.IsNullOrEmpty(layer2) && layer1 != layer2)
                {
                    if (_plugin.DoesMergedDashboardExist(vocoreNumber))
                    {
                        // Merged exists, just switch to it
                        string mergedDash = Core.DashboardMerger.GetMergedDashboardName(vocoreNumber);
                        _plugin.ApplyDashboardDirect(vocoreNumber, mergedDash);
                    }
                    else
                    {
                        // Merged doesn't exist, create it (ApplyDashboardMerged handles everything)
                        _plugin.ApplyDashboardMerged(vocoreNumber, layer1, layer2);
                    }
                }

                GetApplyButton(vocoreNumber).IsEnabled = false;
            }
        }

        /// <summary>
        /// Handle VoCore Layer 1 dropdown selection changed
        /// </summary>
        private void OnVoCoreLayer1SelectionChanged(int vocoreNumber)
        {
            if (_plugin == null || _settings == null) return;

            bool is1Layer = GetLayer1Radio(vocoreNumber).IsChecked == true;
            string defaultDash = GetDefaultDashboardName(vocoreNumber);

            string layer1 = GetLayer1ComboBox(vocoreNumber).SelectedItem?.ToString();
            if (layer1 == "Default") layer1 = defaultDash;

            if (string.IsNullOrEmpty(layer1)) return;

            if (is1Layer)
            {
                SetLayerCount(vocoreNumber, 1);
                SetLayer1(vocoreNumber, layer1);
                _plugin.SaveSettings();
                _plugin.ApplyDashboardDirect(vocoreNumber, layer1);
            }
            else
            {
                SetLayer1(vocoreNumber, layer1);
                _plugin.SaveSettings();
                UpdateVoCoreMergeButtonState(vocoreNumber);
            }
        }

        /// <summary>
        /// Handle VoCore Layer 2 selection changed
        /// </summary>
        private void OnVoCoreLayer2SelectionChanged(int vocoreNumber)
        {
            if (_plugin == null || _settings == null) return;

            string defaultDash = GetDefaultDashboardName(vocoreNumber);
            string layer2 = GetLayer2ComboBox(vocoreNumber).SelectedItem?.ToString();
            if (layer2 == "Default") layer2 = defaultDash;

            if (!string.IsNullOrEmpty(layer2))
            {
                SetLayer2(vocoreNumber, layer2);
                _plugin.SaveSettings();
            }

            UpdateVoCoreMergeButtonState(vocoreNumber);
        }

        /// <summary>
        /// Update VoCore merge button enabled state
        /// </summary>
        private void UpdateVoCoreMergeButtonState(int vocoreNumber)
        {
            bool is2Layers = GetLayer2Radio(vocoreNumber).IsChecked == true;
            if (is2Layers)
            {
                GetApplyButton(vocoreNumber).IsEnabled = HasVoCoreChanged(vocoreNumber);
            }
        }

        /// <summary>
        /// Handle VoCore Merge button click
        /// </summary>
        private void OnVoCoreApply(int vocoreNumber)
        {
            if (_plugin == null || _settings == null) return;

            string defaultDash = GetDefaultDashboardName(vocoreNumber);

            string layer1 = GetLayer1ComboBox(vocoreNumber).SelectedItem?.ToString();
            string layer2 = GetLayer2ComboBox(vocoreNumber).SelectedItem?.ToString();

            if (string.IsNullOrEmpty(layer1) || layer1 == "Default") layer1 = defaultDash;
            if (string.IsNullOrEmpty(layer2) || layer2 == "Default") layer2 = defaultDash;

            if (layer1 == layer2)
            {
                ShowToast("Layers must be different");
                return;
            }

            SetLayerCount(vocoreNumber, 2);
            SetLayer1(vocoreNumber, layer1);
            SetLayer2(vocoreNumber, layer2);
            _plugin.SaveSettings();

            _plugin.ApplyDashboardMerged(vocoreNumber, layer1, layer2);

            string layer1Display = layer1 == defaultDash ? "Default" : layer1;
            string layer2Display = layer2 == defaultDash ? "Default" : layer2;
            UpdateOriginalValues(vocoreNumber, layer1Display, layer2Display);
            GetApplyButton(vocoreNumber).IsEnabled = false;

            ShowToast("Dashboards merged successfully");
        }

        #endregion

        #region Quick Replies Tab


        /// <summary>
        /// 🔍 DEBUG: Discover all available methods in PluginManager
        /// </summary>
        private void DiscoverPluginManagerMethods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_plugin?.PluginManager == null)
                {
                    MessageBox.Show("PluginManager not available!", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var pluginManagerType = _plugin.PluginManager.GetType();
                var output = new System.Text.StringBuilder();

                output.AppendLine("🔍 DISCOVERED PLUGINMANAGER API:");
                output.AppendLine("=====================================");
                output.AppendLine();

                // ======= METHODS =======
                output.AppendLine("📌 MÉTODOS RELEVANTES:");
                output.AppendLine("─────────────────────────────────────");

                var allMethods = pluginManagerType.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Static
                );

                var relevantMethods = allMethods
                    .Where(m =>
                        m.Name.IndexOf("Control", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Configure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Mapping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Bind", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Show", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Open", StringComparison.OrdinalIgnoreCase) >= 0
                    )
                    .OrderBy(m => m.Name)
                    .ToList();

                foreach (var method in relevantMethods)
                {
                    var parameters = method.GetParameters();
                    var paramString = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));

                    output.AppendLine($"✅ {method.Name}({paramString})");
                    output.AppendLine($"   Returns: {method.ReturnType.Name}");
                    output.AppendLine();
                }

                // ======= PROPRIEDADES =======
                output.AppendLine();
                output.AppendLine("📌 PROPRIEDADES RELEVANTES:");
                output.AppendLine("─────────────────────────────────────");

                var allProperties = pluginManagerType.GetProperties(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance
                );

                var relevantProperties = allProperties
                    .Where(p =>
                        p.Name.IndexOf("Control", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.Name.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.Name.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.Name.IndexOf("Mapping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.Name.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0
                    )
                    .OrderBy(p => p.Name)
                    .ToList();

                foreach (var prop in relevantProperties)
                {
                    output.AppendLine($"🔹 {prop.Name}");
                    output.AppendLine($"   Type: {prop.PropertyType.Name}");
                    output.AppendLine($"   CanRead: {prop.CanRead}, CanWrite: {prop.CanWrite}");
                    output.AppendLine();
                }

                // ======= AVAILABLE TYPES IN ASSEMBLY =======
                output.AppendLine();
                output.AppendLine("📌 TIPOS RELACIONADOS COM INPUT/CONTROL NA ASSEMBLY:");
                output.AppendLine("─────────────────────────────────────");

                var assembly = pluginManagerType.Assembly;
                var inputTypes = assembly.GetTypes()
                    .Where(t =>
                        t.Name.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Control", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Mapping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        t.Name.IndexOf("Bind", StringComparison.OrdinalIgnoreCase) >= 0
                    )
                    .OrderBy(t => t.Name)
                    .Take(50)  // Limit to 50 to not get too big
                    .ToList();

                foreach (var type in inputTypes)
                {
                    output.AppendLine($"🔸 {type.FullName}");
                    output.AppendLine($"   IsClass: {type.IsClass}, IsInterface: {type.IsInterface}");
                    output.AppendLine();
                }

                // Mostrar resultados numa janela scrollable
                var window = new Window
                {
                    Title = "PluginManager API Discovery",
                    Width = 900,
                    Height = 700,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = output.ToString(),
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            FontSize = 11,
                            Padding = new Thickness(15),
                            TextWrapping = TextWrapping.Wrap,
                            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                            Foreground = new SolidColorBrush(Colors.White)
                        }
                    },
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
                };

                window.ShowDialog();

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error discovering API: {ex.Message}\n\n{ex.StackTrace}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion


        /// <summary>
        /// Add contact manually - verifies WhatsApp before adding
        /// </summary>
        private void AddManualButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingWhatsApp)
            {
                ShowToast("Please wait, checking WhatsApp...", "⏳", 3);
                return;
            }

            string name = ContactsTab.ManualNameTextBoxCtrl.Text.Trim();
            string number = ContactsTab.ManualNumberTextBoxCtrl.Text.Trim();

            // Validate name
            if (string.IsNullOrWhiteSpace(name) || name == "Name")
            {
                ShowToast("Please enter a name.", "⚠️", 5);
                return;
            }

            // Validate number
            if (string.IsNullOrWhiteSpace(number) || number == "+351912345678" || !number.StartsWith("+"))
            {
                ShowToast("Please enter a valid phone number.\n\nFormat: +[country code][number]\nExample: +351912345678", "⚠️", 8);
                return;
            }

            // Check if already exists
            var existing = _contacts.FirstOrDefault(c =>
                c.Number.Replace("+", "").Replace(" ", "").Replace("-", "") ==
                number.Replace("+", "").Replace(" ", "").Replace("-", ""));

            if (existing != null)
            {
                ShowToast("A contact with this number already exists.", "ℹ️", 5);
                return;
            }

            // Prepare contact to add after verification
            _pendingContactToAdd = new Contact
            {
                Name = name,
                Number = number,
                IsVip = false
            };

            // Show checking state
            _isCheckingWhatsApp = true;
            ContactsTab.AddManualButtonCtrl.IsEnabled = false;
            ContactsTab.AddManualButtonCtrl.Content = "Checking...";

            // Verify if number has WhatsApp
            _plugin.CheckWhatsAppNumber(number);
        }

        /// <summary>
        /// Remover contacto
        /// </summary>
        private void RemoveContactButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var contact = button?.Tag as Contact;

            if (contact == null) return;

            var confirmed = ConfirmDialog.Show(
                $"Remove {contact.Name} from allowed contacts?",
                "They will no longer be able to send you messages.",
                "Remove Contact",
                "Remove",
                "Cancel",
                true);

            if (confirmed)
            {
                _contacts.Remove(contact);
                _plugin.SaveSettings();
            }
        }

        private void ContactsTab_RemoveContactRequested(object sender, Contact contact)
        {
            if (contact == null) return;

            var confirmed = ConfirmDialog.Show(
                $"Remove {contact.Name} from allowed contacts?",
                "They will no longer be able to send you messages.",
                "Remove Contact",
                "Remove",
                "Cancel",
                true);

            if (confirmed)
            {
                _contacts.Remove(contact);
                _plugin.SaveSettings();
            }
        }

        /// <summary>
        /// Backend Mode ComboBox changed
        /// </summary>
        private async void BackendModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConnectionTab.BackendModeComboCtrl.SelectedItem == null) return;

            var selected = ConnectionTab.BackendModeComboCtrl.SelectedItem as ComboBoxItem;
            var newMode = selected?.Tag?.ToString();

            if (string.IsNullOrEmpty(newMode)) return;
            if (_settings.BackendMode == newMode) return; // No change

            _settings.BackendMode = newMode;
            _plugin.SaveSettings();

            // Auto switch backend
            try
            {
                // Show message that will reconnect
                ShowToast($"Switching to {selected.Content}...", "🔄", 3);

                // Switch backend (stop, wait, create new, start)
                await _plugin.SwitchBackend(newMode);

                ShowToast($"{selected.Content} connected successfully!", "✅", 5);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error switching backend: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Debug Logging checkbox changed
        /// </summary>
        private void DebugLoggingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = ConnectionTab.DebugLoggingCheckBoxCtrl.IsChecked == true;
            SaveDebugLoggingState(enabled);
            ShowToast(enabled ? "Debug logging enabled" : "Debug logging disabled", "🔧", 3);
        }

        /// <summary>
        /// Get the debug.json file path
        /// </summary>
        private string GetDebugConfigPath()
        {
            var pluginPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimHub", "WhatsAppPlugin", "config", "debug.json");
            return pluginPath;
        }

        /// <summary>
        /// Load debug logging state from config file
        /// </summary>
        private bool LoadDebugLoggingState()
        {
            try
            {
                var path = GetDebugConfigPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var obj = JObject.Parse(json);
                    return obj["enabled"]?.ToObject<bool>() ?? false;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Save debug logging state to config file
        /// </summary>
        private void SaveDebugLoggingState(bool enabled)
        {
            try
            {
                var path = GetDebugConfigPath();
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var obj = new JObject { ["enabled"] = enabled };
                File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));

                // Also invalidate the cache in WhatsAppPlugin so it re-reads the file
                _plugin?.InvalidateDebugLoggingCache();
            }
            catch { }
        }

        /// <summary>
        /// VIP checkbox changed - save settings
        /// </summary>
        private void ContactsTab_VipCheckboxChanged()
        {
            _plugin?.SaveSettings();
        }

        /// <summary>
        /// Atualizar empty state
        /// </summary>
        private void UpdateContactsEmptyState()
        {
            if (_contacts == null) return;

            ContactsTab.ContactsDataGridCtrl.Visibility = _contacts.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 🔍 EXPLORE SIMHUB API - Discover available methods
        /// </summary>
        /// <summary>
        /// ✅ Create ControlsEditor dynamically via reflection
        /// This avoids compilation errors if the type doesn't exist
        /// </summary>
        private void CreateControlsEditors()
        {
            try
            {
                // Tentar encontrar o assembly SimHub.Plugins
                var simhubPluginsAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "SimHub.Plugins");

                if (simhubPluginsAssembly == null)
                {
                    WriteDebugLog("[ControlsEditor] SimHub.Plugins assembly not found");
                    return;
                }

                // Tentar encontrar o tipo ControlsEditor
                var controlsEditorType = simhubPluginsAssembly.GetType("SimHub.Plugins.UI.ControlsEditor");

                if (controlsEditorType == null)
                {
                    WriteDebugLog("[ControlsEditor] ControlsEditor type not found");
                    return;
                }

                // Create instance for Reply1
                var reply1Editor = Activator.CreateInstance(controlsEditorType);
                if (reply1Editor != null)
                {
                    // Configure properties
                    // ⚡ CRITICAL: ControlsEditor does NOT add prefix automatically!
                    // We need to use the COMPLETE name: "WhatsAppPlugin.SendReply1"
                    controlsEditorType.GetProperty("ActionName")?.SetValue(reply1Editor, "WhatsAppPlugin.SendReply1");

                    // ✅ Replace ContentPresenter content
                    if (QuickRepliesTab.Reply1ControlEditorPlaceholderCtrl != null)
                    {
                        QuickRepliesTab.Reply1ControlEditorPlaceholderCtrl.Content = reply1Editor;
                    }

                    WriteDebugLog("[ControlsEditor] Reply1 editor created successfully");
                }

                // Create instance for Reply2
                var reply2Editor = Activator.CreateInstance(controlsEditorType);
                if (reply2Editor != null)
                {
                    // Configure properties
                    // ⚡ CRITICAL: ControlsEditor does NOT add prefix automatically!
                    // We need to use the COMPLETE name: "WhatsAppPlugin.SendReply2"
                    controlsEditorType.GetProperty("ActionName")?.SetValue(reply2Editor, "WhatsAppPlugin.SendReply2");

                    // ✅ Replace ContentPresenter content
                    if (QuickRepliesTab.Reply2ControlEditorPlaceholderCtrl != null)
                    {
                        QuickRepliesTab.Reply2ControlEditorPlaceholderCtrl.Content = reply2Editor;
                    }

                    WriteDebugLog("[ControlsEditor] Reply2 editor created successfully");
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[ControlsEditor] Error: {ex.Message}");
                // Do nothing - let placeholders show default message
            }
        }

        private void WriteDebugLog(string message)
        {
            try
            {
                // Only write logs if debug is enabled
                if (!LoadDebugLoggingState())
                    return;

                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "logs", "ui-debug.log");

                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        #region Backend Libraries Management

        private static readonly HttpClient _httpClient = new HttpClient();
        private List<string> _whatsappWebJsVersions = new List<string>();
        private List<string> _baileysVersions = new List<string>();
        private string _latestWwjsScriptVersion = null;
        private string _latestBaileysScriptVersion = null;
        private string _latestGoogleScriptVersion = null;

        /// <summary>
        /// Initialize backend library settings from saved config
        /// </summary>
        private void LoadBackendLibrarySettings()
        {
            // WhatsApp-Web.js source
            if (_settings.WhatsAppWebJsSource == "manual")
            {
                ConnectionTab.WhatsAppWebJsManualRadioCtrl.IsChecked = true;
                ConnectionTab.WhatsAppWebJsManualPanelCtrl.Visibility = Visibility.Visible;
                ConnectionTab.WhatsAppWebJsRepoTextBoxCtrl.Text = _settings.WhatsAppWebJsManualRepo;

                // Disable version dropdown and check button when manual
                ConnectionTab.WhatsAppWebJsVersionComboCtrl.IsEnabled = false;
                ConnectionTab.WhatsAppWebJsCheckButtonCtrl.IsEnabled = false;
            }
            else
            {
                ConnectionTab.WhatsAppWebJsOfficialRadioCtrl.IsChecked = true;
                ConnectionTab.WhatsAppWebJsManualPanelCtrl.Visibility = Visibility.Collapsed;
            }

            // Baileys source
            if (_settings.BaileysSource == "manual")
            {
                ConnectionTab.BaileysManualRadioCtrl.IsChecked = true;
                ConnectionTab.BaileysManualPanelCtrl.Visibility = Visibility.Visible;
                ConnectionTab.BaileysRepoTextBoxCtrl.Text = _settings.BaileysManualRepo;

                // Disable version dropdown and check button when manual
                ConnectionTab.BaileysVersionComboCtrl.IsEnabled = false;
                ConnectionTab.BaileysCheckButtonCtrl.IsEnabled = false;
            }
            else
            {
                ConnectionTab.BaileysOfficialRadioCtrl.IsChecked = true;
                ConnectionTab.BaileysManualPanelCtrl.Visibility = Visibility.Collapsed;
            }

            // Load current installed versions
            LoadInstalledVersions();

            // Auto-check available versions on startup (fire and forget)
            _ = AutoCheckVersionsOnStartupAsync();
        }

        /// <summary>
        /// Auto-check versions for both whatsapp-web.js and baileys on plugin startup
        /// </summary>
        private async Task AutoCheckVersionsOnStartupAsync()
        {
            try
            {
                WriteDebugLog("[AutoCheckVersions] Starting auto-check...");

                // Small delay to let UI load first
                await Task.Delay(1000);

                // Only check versions for backends that are NOT using manual source
                var tasks = new List<Task>();

                WriteDebugLog($"[AutoCheckVersions] WhatsAppWebJsSource: {_settings.WhatsAppWebJsSource}");
                WriteDebugLog($"[AutoCheckVersions] BaileysSource: {_settings.BaileysSource}");

                if (_settings.WhatsAppWebJsSource != "manual")
                {
                    WriteDebugLog("[AutoCheckVersions] Fetching whatsapp-web.js versions...");
                    tasks.Add(FetchWhatsAppWebJsVersionsAsync());
                }

                if (_settings.BaileysSource != "manual")
                {
                    WriteDebugLog("[AutoCheckVersions] Fetching baileys versions...");
                    tasks.Add(FetchBaileysVersionsAsync());
                }

                // Always check scripts version
                WriteDebugLog("[AutoCheckVersions] Checking scripts version...");
                tasks.Add(CheckScriptsVersionOnStartupAsync());

                await Task.WhenAll(tasks);
                WriteDebugLog("[AutoCheckVersions] Completed.");
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[AutoCheckVersions] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetch whatsapp-web.js versions and update dropdown (without UI feedback)
        /// </summary>
        private async Task FetchWhatsAppWebJsVersionsAsync()
        {
            try
            {
                WriteDebugLog("[FetchWhatsAppWebJsVersions] Fetching from npm...");
                var versions = await FetchNpmVersionsAsync("whatsapp-web.js");
                WriteDebugLog($"[FetchWhatsAppWebJsVersions] Got {versions.Count} versions");

                if (versions.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _whatsappWebJsVersions = versions;
                        var currentVersion = _settings.WhatsAppWebJsVersion;

                        // Get existing items to avoid duplicates
                        var existingTags = ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items
                            .Cast<ComboBoxItem>()
                            .Select(i => i.Tag?.ToString())
                            .Where(t => t != null)
                            .ToHashSet();

                        // Add versions from npm (latest 10) that don't already exist
                        foreach (var version in versions.Take(10))
                        {
                            if (!existingTags.Contains(version))
                            {
                                var item = new ComboBoxItem
                                {
                                    Content = version,
                                    Tag = version
                                };
                                ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Add(item);
                            }
                        }

                    });
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[FetchWhatsAppWebJsVersions] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetch baileys versions and update dropdown (without UI feedback)
        /// </summary>
        private async Task FetchBaileysVersionsAsync()
        {
            try
            {
                WriteDebugLog("[FetchBaileysVersions] Fetching from GitHub...");
                // Fetch versions from GitHub tags (Baileys v7+ is on GitHub)
                var allVersions = await FetchGitHubTagsAsync("WhiskeySockets", "Baileys");
                WriteDebugLog($"[FetchBaileysVersions] Got {allVersions.Count} tags");

                // Filter v7+ versions
                var versions = allVersions
                    .Select(v => v.TrimStart('v'))
                    .Where(v =>
                    {
                        if (string.IsNullOrEmpty(v)) return false;
                        var firstPart = v.Split('.')[0].Split('-')[0];
                        return int.TryParse(firstPart, out int major) && major >= 7;
                    })
                    .ToList();

                if (versions.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _baileysVersions = versions;
                        var currentVersion = _settings.BaileysVersion;

                        // Get currently selected tag to restore later
                        var currentlySelectedTag = (ConnectionTab.BaileysVersionComboCtrl.SelectedItem as ComboBoxItem)?.Tag?.ToString();

                        // Clear and rebuild dropdown with versions sorted
                        ConnectionTab.BaileysVersionComboCtrl.Items.Clear();

                        // 1. Add @latest first
                        var latestItem = new ComboBoxItem
                        {
                            Content = "@latest (latest version)",
                            Tag = "npm:@whiskeysockets/baileys@latest"
                        };
                        ConnectionTab.BaileysVersionComboCtrl.Items.Add(latestItem);

                        // 2. Add v7.x versions (latest 10)
                        foreach (var version in versions.Take(10))
                        {
                            var item = new ComboBoxItem
                            {
                                Content = version,
                                Tag = version
                            };
                            ConnectionTab.BaileysVersionComboCtrl.Items.Add(item);
                        }

                        // 3. Restore selection
                        foreach (ComboBoxItem item in ConnectionTab.BaileysVersionComboCtrl.Items)
                        {
                            if (item.Tag?.ToString() == currentlySelectedTag ||
                                (currentlySelectedTag?.Contains("@latest") == true && item.Tag?.ToString()?.Contains("@latest") == true))
                            {
                                ConnectionTab.BaileysVersionComboCtrl.SelectedItem = item;
                                break;
                            }
                        }

                        // If nothing was selected, select @latest
                        if (ConnectionTab.BaileysVersionComboCtrl.SelectedItem == null)
                        {
                            ConnectionTab.BaileysVersionComboCtrl.SelectedIndex = 0;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[FetchBaileysVersions] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check scripts versions on startup and show update buttons if newer versions available
        /// </summary>
        private async Task CheckScriptsVersionOnStartupAsync()
        {
            try
            {
                // Check whatsapp-server.js
                var wwjsLocalVersion = GetLocalScriptVersion("whatsapp-server.js");
                var wwjsGithubVersion = await FetchGitHubScriptVersionAsync("whatsapp-server.js");
                WriteDebugLog($"[CheckScriptsVersionOnStartup] whatsapp-server.js - Local: {wwjsLocalVersion}, GitHub: {wwjsGithubVersion}");

                if (wwjsGithubVersion != null && wwjsLocalVersion != wwjsGithubVersion)
                {
                    _latestWwjsScriptVersion = wwjsGithubVersion;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ConnectionTab.WwjsScriptUpdateButtonCtrl.Visibility = Visibility.Visible;
                    });
                    WriteDebugLog($"[CheckScriptsVersionOnStartup] whatsapp-server.js update available: {wwjsLocalVersion} -> {wwjsGithubVersion}");
                }

                // Check baileys-server.mjs
                var baileysLocalVersion = GetLocalScriptVersion("baileys-server.mjs");
                var baileysGithubVersion = await FetchGitHubScriptVersionAsync("baileys-server.mjs");
                WriteDebugLog($"[CheckScriptsVersionOnStartup] baileys-server.mjs - Local: {baileysLocalVersion}, GitHub: {baileysGithubVersion}");

                if (baileysGithubVersion != null && baileysLocalVersion != baileysGithubVersion)
                {
                    _latestBaileysScriptVersion = baileysGithubVersion;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ConnectionTab.BaileysScriptUpdateButtonCtrl.Visibility = Visibility.Visible;
                    });
                    WriteDebugLog($"[CheckScriptsVersionOnStartup] baileys-server.mjs update available: {baileysLocalVersion} -> {baileysGithubVersion}");
                }

                // Check google-contacts.js
                var googleLocalVersion = GetLocalScriptVersion("google-contacts.js");
                var googleGithubVersion = await FetchGitHubScriptVersionAsync("google-contacts.js");
                WriteDebugLog($"[CheckScriptsVersionOnStartup] google-contacts.js - Local: {googleLocalVersion}, GitHub: {googleGithubVersion}");

                if (googleGithubVersion != null && googleLocalVersion != googleGithubVersion)
                {
                    _latestGoogleScriptVersion = googleGithubVersion;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ConnectionTab.GoogleScriptUpdateButtonCtrl.Visibility = Visibility.Visible;
                    });
                    WriteDebugLog($"[CheckScriptsVersionOnStartup] google-contacts.js update available: {googleLocalVersion} -> {googleGithubVersion}");
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[CheckScriptsVersionOnStartup] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load currently installed library versions from package.json (what's configured)
        /// </summary>
        private void LoadInstalledVersions()
        {
            try
            {
                var nodePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node");

                var packageJsonPath = Path.Combine(nodePath, "package.json");

                if (File.Exists(packageJsonPath))
                {
                    var packageJson = JObject.Parse(File.ReadAllText(packageJsonPath));
                    var deps = packageJson["dependencies"] as JObject;

                    if (deps != null)
                    {
                        // WhatsApp-Web.js - ler do package.json
                        var wwjsSpec = deps["whatsapp-web.js"]?.ToString();
                        ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Clear();

                        if (!string.IsNullOrEmpty(wwjsSpec))
                        {
                            string displayVersion = wwjsSpec;
                            string tagVersion = wwjsSpec;

                            // If github:main, show in friendly format
                            if (wwjsSpec.Contains("github:") && wwjsSpec.Contains("#main"))
                            {
                                displayVersion = "github:main (latest dev)";
                                tagVersion = "github:pedroslopez/whatsapp-web.js#main";
                            }
                            // If normal version, remove ^ if exists
                            else if (wwjsSpec.StartsWith("^"))
                            {
                                displayVersion = wwjsSpec.Substring(1);
                                tagVersion = wwjsSpec.Substring(1);
                            }

                            _settings.WhatsAppWebJsVersion = tagVersion;

                            // Add github:main option
                            var mainItem = new ComboBoxItem
                            {
                                Content = "github:main (latest dev)",
                                Tag = "github:pedroslopez/whatsapp-web.js#main"
                            };
                            ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Add(mainItem);
                            if (tagVersion.Contains("#main")) mainItem.IsSelected = true;

                            // Add installed version if not github:main
                            if (!tagVersion.Contains("#main"))
                            {
                                var installedItem = new ComboBoxItem
                                {
                                    Content = $"{displayVersion} (installed)",
                                    Tag = tagVersion,
                                    IsSelected = true
                                };
                                ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Add(installedItem);
                            }
                        }

                        // Baileys - ler do package.json
                        var baileysSpec = deps["@whiskeysockets/baileys"]?.ToString();
                        ConnectionTab.BaileysVersionComboCtrl.Items.Clear();

                        if (!string.IsNullOrEmpty(baileysSpec))
                        {
                            string displayVersion = baileysSpec;
                            string tagVersion = baileysSpec;

                            // Se for npm:@whiskeysockets/baileys@latest
                            if (baileysSpec.Contains("@latest"))
                            {
                                displayVersion = "@latest (latest version)";
                                tagVersion = "npm:@whiskeysockets/baileys@latest";
                            }
                            // If normal version, remove npm: and ^ if exists
                            else
                            {
                                displayVersion = baileysSpec.Replace("npm:@whiskeysockets/baileys@", "").Replace("^", "");
                                tagVersion = displayVersion;
                            }

                            _settings.BaileysVersion = tagVersion;

                            // Add @latest option
                            var latestItem = new ComboBoxItem
                            {
                                Content = "@latest (latest version)",
                                Tag = "npm:@whiskeysockets/baileys@latest"
                            };
                            ConnectionTab.BaileysVersionComboCtrl.Items.Add(latestItem);
                            if (tagVersion.Contains("@latest")) latestItem.IsSelected = true;

                            // Add installed version if not @latest
                            if (!tagVersion.Contains("@latest"))
                            {
                                var installedItem = new ComboBoxItem
                                {
                                    Content = $"{displayVersion} (installed)",
                                    Tag = tagVersion,
                                    IsSelected = true
                                };
                                ConnectionTab.BaileysVersionComboCtrl.Items.Add(installedItem);
                            }
                        }
                    }
                }

                // Scripts versions
                var wwjsScriptVersion = GetLocalScriptVersion("whatsapp-server.js");
                ConnectionTab.WwjsScriptVersionTextCtrl.Text = wwjsScriptVersion ?? "N/A";

                var baileysScriptVersion = GetLocalScriptVersion("baileys-server.mjs");
                ConnectionTab.BaileysScriptVersionTextCtrl.Text = baileysScriptVersion ?? "N/A";

                var googleScriptVersion = GetLocalScriptVersion("google-contacts.js");
                ConnectionTab.GoogleScriptVersionTextCtrl.Text = googleScriptVersion ?? "N/A";
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[BackendLibraries] Error loading versions: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for whatsapp-web.js updates from npm registry
        /// </summary>
        private async void WhatsAppWebJsCheckButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionTab.WhatsAppWebJsCheckButtonCtrl.IsEnabled = false;
            ConnectionTab.WhatsAppWebJsCheckButtonCtrl.Content = "Checking...";

            try
            {
                var versions = await FetchNpmVersionsAsync("whatsapp-web.js");

                if (versions.Count > 0)
                {
                    _whatsappWebJsVersions = versions;
                    var currentVersion = _settings.WhatsAppWebJsVersion;

                    // Guardar item selecionado atual
                    var currentlySelected = ConnectionTab.WhatsAppWebJsVersionComboCtrl.SelectedItem as ComboBoxItem;
                    var existingItems = ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Cast<ComboBoxItem>().ToList();

                    // Add npm versions (last 10 v7.x) that don't exist yet
                    foreach (var version in versions.Take(10))
                    {
                        // Check if already exists in dropdown
                        var exists = existingItems.Any(item => item.Tag?.ToString() == version);

                        if (!exists)
                        {
                            var item = new ComboBoxItem
                            {
                                Content = version,
                                Tag = version
                            };
                            ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Add(item);
                        }
                    }

                    // Restore selection
                    if (currentlySelected != null)
                    {
                        ConnectionTab.WhatsAppWebJsVersionComboCtrl.SelectedItem = currentlySelected;
                    }

                    ShowToast("whatsapp-web.js versions loaded!", "✅", 3);
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error checking updates: {ex.Message}", "❌", 5);
            }
            finally
            {
                ConnectionTab.WhatsAppWebJsCheckButtonCtrl.IsEnabled = true;
                ConnectionTab.WhatsAppWebJsCheckButtonCtrl.Content = "Check for updates";
            }
        }

        /// <summary>
        /// Check for baileys updates from npm registry
        /// </summary>
        private async void BaileysCheckButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionTab.BaileysCheckButtonCtrl.IsEnabled = false;
            ConnectionTab.BaileysCheckButtonCtrl.Content = "Checking...";

            try
            {
                // Fetch versions from GitHub tags (Baileys v7+ is on GitHub, not npm)
                var allVersions = await FetchGitHubTagsAsync("WhiskeySockets", "Baileys");

                // Filter v7+ versions (7.x, 8.x, 9.x, etc.)
                var versions = allVersions
                    .Select(v => v.TrimStart('v'))
                    .Where(v =>
                    {
                        if (string.IsNullOrEmpty(v)) return false;
                        var firstPart = v.Split('.')[0].Split('-')[0];
                        return int.TryParse(firstPart, out int major) && major >= 7;
                    })
                    .ToList();

                if (versions.Count > 0)
                {
                    _baileysVersions = versions;
                    var currentVersion = _settings.BaileysVersion;

                    // Guardar item selecionado atual
                    var currentlySelectedTag = (ConnectionTab.BaileysVersionComboCtrl.SelectedItem as ComboBoxItem)?.Tag?.ToString();

                    // Clear and rebuild dropdown with sorted versions
                    ConnectionTab.BaileysVersionComboCtrl.Items.Clear();

                    // 1. Add @latest first
                    var latestItem = new ComboBoxItem
                    {
                        Content = "@latest (latest version)",
                        Tag = "npm:@whiskeysockets/baileys@latest"
                    };
                    ConnectionTab.BaileysVersionComboCtrl.Items.Add(latestItem);

                    // 2. Add v7.x versions from npm (last 10)
                    foreach (var version in versions.Take(10))
                    {
                        var item = new ComboBoxItem
                        {
                            Content = version,
                            Tag = version
                        };
                        ConnectionTab.BaileysVersionComboCtrl.Items.Add(item);
                    }

                    // 3. Restore selection
                    foreach (ComboBoxItem item in ConnectionTab.BaileysVersionComboCtrl.Items)
                    {
                        if (item.Tag?.ToString() == currentlySelectedTag ||
                            (currentlySelectedTag?.Contains("@latest") == true && item.Tag?.ToString()?.Contains("@latest") == true))
                        {
                            ConnectionTab.BaileysVersionComboCtrl.SelectedItem = item;
                            break;
                        }
                    }

                    // Se nada foi selecionado, selecionar @latest
                    if (ConnectionTab.BaileysVersionComboCtrl.SelectedItem == null)
                    {
                        ConnectionTab.BaileysVersionComboCtrl.SelectedIndex = 0;
                    }

                    ShowToast("baileys versions loaded!", "✅", 3);
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error checking updates: {ex.Message}", "❌", 5);
            }
            finally
            {
                ConnectionTab.BaileysCheckButtonCtrl.IsEnabled = true;
                ConnectionTab.BaileysCheckButtonCtrl.Content = "Check for updates";
            }
        }

        /// <summary>
        /// Fetch versions from npm registry
        /// </summary>
        private async Task<List<string>> FetchNpmVersionsAsync(string packageName)
        {
            var versions = new List<string>();

            try
            {
                var url = $"https://registry.npmjs.org/{Uri.EscapeDataString(packageName)}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                var versionsObj = json["versions"] as JObject;
                if (versionsObj != null)
                {
                    // Get all stable versions and sort descending (fetch more for filtering later)
                    versions = versionsObj.Properties()
                        .Select(p => p.Name)
                        .Where(v => !v.Contains("-")) // Exclude pre-release versions (alpha, beta, rc)
                        .OrderByDescending(v =>
                        {
                            try
                            {
                                return new Version(Regex.Replace(v, @"[^\d.]", "").TrimEnd('.'));
                            }
                            catch
                            {
                                return new Version("0.0.0");
                            }
                        })
                        .Take(50) // Fetch more to allow filtering
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[FetchNpmVersions] Error for {packageName}: {ex.Message}");
            }

            return versions;
        }

        /// <summary>
        /// Fetch tags from GitHub repository
        /// </summary>
        private async Task<List<string>> FetchGitHubTagsAsync(string owner, string repo)
        {
            var tags = new List<string>();

            try
            {
                var url = $"https://api.github.com/repos/{owner}/{repo}/tags?per_page=50";

                // GitHub API requires User-Agent header
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "WhatsAppSimHubPlugin");

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                var json = JArray.Parse(content);

                tags = json
                    .Select(t => t["name"]?.ToString())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[FetchGitHubTags] Error for {owner}/{repo}: {ex.Message}");
            }

            return tags;
        }

        /// <summary>
        /// Handle version selection change for whatsapp-web.js
        /// </summary>
        private void WhatsAppWebJsVersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConnectionTab.WhatsAppWebJsVersionComboCtrl.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var selectedVersion = item.Tag.ToString();

                // Show/hide Install button based on selection
                if (selectedVersion != _settings.WhatsAppWebJsVersion)
                {
                    ConnectionTab.WhatsAppWebJsInstallButtonCtrl.Visibility = Visibility.Visible;
                }
                else
                {
                    ConnectionTab.WhatsAppWebJsInstallButtonCtrl.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Handle version selection change for baileys
        /// </summary>
        private void BaileysVersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConnectionTab.BaileysVersionComboCtrl.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var selectedVersion = item.Tag.ToString();

                // Show/hide Install button based on selection
                if (selectedVersion != _settings.BaileysVersion)
                {
                    ConnectionTab.BaileysInstallButtonCtrl.Visibility = Visibility.Visible;
                }
                else
                {
                    ConnectionTab.BaileysInstallButtonCtrl.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Install a specific library version
        /// </summary>
        private async Task InstallLibraryVersionAsync(string packageName, string version)
        {
            try
            {
                ShowToast($"Installing {packageName}@{version}...", "📦", 10);

                // Stop current connection gracefully
                _plugin.DisconnectWhatsApp();
                await Task.Delay(2000); // Wait for graceful shutdown

                // Get node folder path
                var nodePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node");

                // Run npm install - use cmd.exe /c because npm is a batch script on Windows
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c npm install {packageName}@{version}",
                    WorkingDirectory = nodePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    var error = await errorTask;

                    if (process.ExitCode == 0)
                    {
                        // Update settings
                        if (packageName == "whatsapp-web.js")
                        {
                            _settings.WhatsAppWebJsVersion = version;
                        }
                        else if (packageName.Contains("baileys"))
                        {
                            _settings.BaileysVersion = version;
                        }

                        _plugin.SaveSettings();
                        LoadInstalledVersions();

                        ShowToast($"{packageName}@{version} installed successfully!", "✅", 5);
                    }
                    else
                    {
                        ShowToast($"npm install failed: {error}", "❌", 10);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error installing library: {ex.Message}", "❌", 10);
            }
        }

        /// <summary>
        /// Handle Install button click for whatsapp-web.js
        /// </summary>
        private void WhatsAppWebJsInstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionTab.WhatsAppWebJsVersionComboCtrl.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var selectedVersion = item.Tag.ToString();

                var confirmed = ConfirmDialog.Show(
                    $"Install whatsapp-web.js {selectedVersion}?",
                    "The plugin will:\n" +
                    "1. Disconnect from WhatsApp\n" +
                    "2. Stop all Node.js and Chrome processes\n" +
                    "3. Delete node_modules and reinstall\n" +
                    "4. Reconnect automatically when done",
                    "Install Library",
                    "Install",
                    "Cancel");

                if (confirmed)
                {
                    InstallLibraryInBackground("whatsapp-web.js", selectedVersion);
                }
            }
        }

        /// <summary>
        /// Handle Install button click for baileys
        /// </summary>
        private void BaileysInstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectionTab.BaileysVersionComboCtrl.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var selectedVersion = item.Tag.ToString();

                var confirmed = ConfirmDialog.Show(
                    $"Install baileys {selectedVersion}?",
                    "The plugin will:\n" +
                    "1. Disconnect from WhatsApp\n" +
                    "2. Stop all Node.js and Chrome processes\n" +
                    "3. Delete node_modules and reinstall\n" +
                    "4. Reconnect automatically when done",
                    "Install Library",
                    "Install",
                    "Cancel");

                if (confirmed)
                {
                    InstallLibraryInBackground("@whiskeysockets/baileys", selectedVersion);
                }
            }
        }

        /// <summary>
        /// Install library in background WITHOUT restarting SimHub
        /// </summary>
        private async void InstallLibraryInBackground(string packageName, string version, string manualRepo = null)
        {
            try
            {
                var nodePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node");

                var packageJsonPath = Path.Combine(nodePath, "package.json");

                // Show installing status
                _plugin.Settings.DependenciesInstalling = true;
                SetDependenciesInstalling(true, $"Installing {packageName} {version}...");
                ShowToast($"Installing {packageName} {version}...", "🔄", 60);

                // Step 1: Disconnect WhatsApp and update UI status
                _plugin.DisconnectWhatsApp();
                UpdateConnectionStatus("Disconnected");
                await Task.Delay(1000);

                // Step 3: Update package.json
                var json = JObject.Parse(File.ReadAllText(packageJsonPath));
                var deps = json["dependencies"] as JObject;

                if (deps != null)
                {
                    var key = packageName == "whatsapp-web.js" ? "whatsapp-web.js" : "@whiskeysockets/baileys";
                    deps[key] = version;
                    File.WriteAllText(packageJsonPath, json.ToString());
                }

                // Step 4: Delete ENTIRE node_modules folder
                var nodeModulesPath = Path.Combine(nodePath, "node_modules");
                if (Directory.Exists(nodeModulesPath))
                {
                    SetDependenciesInstalling(true, "Deleting node_modules...");
                    await Task.Run(() => Directory.Delete(nodeModulesPath, true));
                    await Task.Delay(500);
                }

                // Step 5: Delete package-lock.json
                var packageLockPath = Path.Combine(nodePath, "package-lock.json");
                if (File.Exists(packageLockPath))
                {
                    File.Delete(packageLockPath);
                }

                // Step 6: Run npm install
                SetDependenciesInstalling(true, "Running npm install...");
                UpdateNpmStatus("Installing...", false);

                // IMPORTANT: On Windows, npm is a batch script (npm.cmd), not an executable
                // We need to use cmd.exe /c to run it properly
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c npm install",
                    WorkingDirectory = nodePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                WriteDebugLog($"[InstallLibrary] Running npm install in {nodePath}");

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    // Capture output for debugging
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    // Use async wait to avoid blocking UI thread
                    await process.WaitForExitAsync();

                    var output = await outputTask;
                    var error = await errorTask;

                    WriteDebugLog($"[InstallLibrary] npm install exit code: {process.ExitCode}");
                    WriteDebugLog($"[InstallLibrary] npm output: {output}");
                    if (!string.IsNullOrEmpty(error))
                        WriteDebugLog($"[InstallLibrary] npm error: {error}");

                    if (process.ExitCode == 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateNpmStatus("Installed", true);
                            ShowToast($"{packageName} {version} installed successfully!", "✅", 5);

                            // Update settings with the new installed version
                            if (packageName == "whatsapp-web.js")
                            {
                                _settings.WhatsAppWebJsVersion = version;
                                ConnectionTab.WhatsAppWebJsInstallButtonCtrl.Visibility = Visibility.Collapsed;

                                // If manual repo install was successful, save the repo
                                if (!string.IsNullOrEmpty(manualRepo))
                                {
                                    _settings.WhatsAppWebJsManualRepo = manualRepo;
                                }
                            }
                            else if (packageName.Contains("baileys"))
                            {
                                _settings.BaileysVersion = version;
                                ConnectionTab.BaileysInstallButtonCtrl.Visibility = Visibility.Collapsed;

                                // If manual repo install was successful, save the repo
                                if (!string.IsNullOrEmpty(manualRepo))
                                {
                                    _settings.BaileysManualRepo = manualRepo;
                                }
                            }
                            _plugin.SaveSettings();
                            LoadInstalledVersions();
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateNpmStatus("Installation failed", false, true);
                            ShowToast($"npm install failed: {error}", "❌", 10);
                        });
                    }

                    process.Dispose();
                }
                else
                {
                    WriteDebugLog("[InstallLibrary] Failed to start npm process!");
                    Dispatcher.Invoke(() =>
                    {
                        UpdateNpmStatus("Failed to start npm", false, true);
                        ShowToast("Failed to start npm process", "❌", 10);
                    });
                }

                // Step 7: Finish
                SetDependenciesInstalling(false);
                _plugin.Settings.DependenciesInstalling = false;

                // Step 8: Reconnect
                await Task.Delay(1000);
                await _plugin.ReconnectWhatsApp();

            }
            catch (Exception ex)
            {
                ShowToast($"Error installing: {ex.Message}", "❌", 10);
                SetDependenciesInstalling(false);
                _plugin.Settings.DependenciesInstalling = false;
            }
        }

        /// <summary>
        /// Handle source radio button change for whatsapp-web.js
        /// </summary>
        private void WhatsAppWebJsSourceRadio_Changed(object sender, RoutedEventArgs e)
        {
            // Skip if not initialized yet (called during InitializeComponent)
            if (_plugin == null || _settings == null) return;

            if (ConnectionTab.WhatsAppWebJsManualRadioCtrl.IsChecked == true)
            {
                ConnectionTab.WhatsAppWebJsManualPanelCtrl.Visibility = Visibility.Visible;
                _settings.WhatsAppWebJsSource = "manual";

                // Disable version dropdown and check button when manual
                ConnectionTab.WhatsAppWebJsVersionComboCtrl.IsEnabled = false;
                ConnectionTab.WhatsAppWebJsCheckButtonCtrl.IsEnabled = false;
            }
            else
            {
                ConnectionTab.WhatsAppWebJsManualPanelCtrl.Visibility = Visibility.Collapsed;
                _settings.WhatsAppWebJsSource = "official";

                // Re-enable version dropdown and check button
                ConnectionTab.WhatsAppWebJsVersionComboCtrl.IsEnabled = true;
                ConnectionTab.WhatsAppWebJsCheckButtonCtrl.IsEnabled = true;

                // If switching back to official and manual was applied, revert
                if (!string.IsNullOrEmpty(_settings.WhatsAppWebJsManualRepo))
                {
                    _settings.WhatsAppWebJsManualRepo = "";
                }
            }
            _plugin.SaveSettings();
        }

        /// <summary>
        /// Handle source radio button change for baileys
        /// </summary>
        private void BaileysSourceRadio_Changed(object sender, RoutedEventArgs e)
        {
            // Skip if not initialized yet (called during InitializeComponent)
            if (_plugin == null || _settings == null) return;

            if (ConnectionTab.BaileysManualRadioCtrl.IsChecked == true)
            {
                ConnectionTab.BaileysManualPanelCtrl.Visibility = Visibility.Visible;
                _settings.BaileysSource = "manual";

                // Disable version dropdown and check button when manual
                ConnectionTab.BaileysVersionComboCtrl.IsEnabled = false;
                ConnectionTab.BaileysCheckButtonCtrl.IsEnabled = false;
            }
            else
            {
                ConnectionTab.BaileysManualPanelCtrl.Visibility = Visibility.Collapsed;
                _settings.BaileysSource = "official";

                // Re-enable version dropdown and check button
                ConnectionTab.BaileysVersionComboCtrl.IsEnabled = true;
                ConnectionTab.BaileysCheckButtonCtrl.IsEnabled = true;

                // If switching back to official and manual was applied, revert
                if (!string.IsNullOrEmpty(_settings.BaileysManualRepo))
                {
                    _settings.BaileysManualRepo = "";
                }
            }
            _plugin.SaveSettings();
        }

        /// <summary>
        /// Apply manual repository for whatsapp-web.js
        /// </summary>
        private async void WhatsAppWebJsApplyRepo_Click(object sender, RoutedEventArgs e)
        {
            var repo = ConnectionTab.WhatsAppWebJsRepoTextBoxCtrl.Text.Trim();

            if (string.IsNullOrEmpty(repo))
            {
                ShowToast("Please enter a repository", "⚠️", 5);
                return;
            }

            // Validate format - must start with github:
            if (!repo.StartsWith("github:"))
            {
                ShowToast("Invalid format. Must start with github: (e.g.: github:user/repo#branch)", "⚠️", 5);
                return;
            }

            // Check if already installed
            if (_settings.WhatsAppWebJsVersion == repo)
            {
                ShowToast($"Repository already installed: {repo}", "ℹ️", 5);
                return;
            }

            // Check if repository exists before asking for confirmation
            ShowToast("Checking if repository exists...", "🔍", 5);
            var repoExists = await CheckGitHubRepoExistsAsync(repo);

            if (!repoExists)
            {
                ShowToast($"Repository not found: {repo}", "❌", 5);
                return;
            }

            var confirmed = ConfirmDialog.Show(
                "Install whatsapp-web.js from GitHub repository?",
                $"Repository: {repo}\n\nThis will:\n- Stop WhatsApp connection\n- Delete node_modules and reinstall\n- Restart connection",
                "Install from Repository",
                "Install",
                "Cancel");

            if (confirmed)
            {
                // Use the same install function as the Install button
                InstallLibraryInBackground("whatsapp-web.js", repo, repo);
            }
        }

        /// <summary>
        /// Apply manual repository for baileys
        /// </summary>
        private async void BaileysApplyRepo_Click(object sender, RoutedEventArgs e)
        {
            var repo = ConnectionTab.BaileysRepoTextBoxCtrl.Text.Trim();

            if (string.IsNullOrEmpty(repo))
            {
                ShowToast("Please enter a repository", "⚠️", 5);
                return;
            }

            // Validate format - must start with github:
            if (!repo.StartsWith("github:"))
            {
                ShowToast("Invalid format. Must start with github: (e.g.: github:user/repo#branch)", "⚠️", 5);
                return;
            }

            // Check if already installed
            if (_settings.BaileysVersion == repo)
            {
                ShowToast($"Repository already installed: {repo}", "ℹ️", 5);
                return;
            }

            // Check if repository exists before asking for confirmation
            ShowToast("Checking if repository exists...", "🔍", 5);
            var repoExists = await CheckGitHubRepoExistsAsync(repo);

            if (!repoExists)
            {
                ShowToast($"Repository not found: {repo}", "❌", 5);
                return;
            }

            var confirmed = ConfirmDialog.Show(
                "Install baileys from GitHub repository?",
                $"Repository: {repo}\n\nThis will:\n- Stop WhatsApp connection\n- Delete node_modules and reinstall\n- Restart connection",
                "Install from Repository",
                "Install",
                "Cancel");

            if (confirmed)
            {
                // Use the same install function as the Install button
                InstallLibraryInBackground("@whiskeysockets/baileys", repo, repo);
            }
        }

        /// <summary>
        /// Check if a GitHub repository exists
        /// </summary>
        private async Task<bool> CheckGitHubRepoExistsAsync(string repo)
        {
            try
            {
                // Remove github: prefix if present
                var cleanRepo = repo.StartsWith("github:") ? repo.Substring(7) : repo;

                // Remove branch suffix if present (e.g., #main)
                var repoPath = cleanRepo.Split('#')[0];

                var url = $"https://api.github.com/repos/{repoPath}";

                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "WhatsAppSimHubPlugin");

                var response = await _httpClient.SendAsync(request);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check for scripts updates from GitHub
        /// </summary>
        private async void ScriptsCheckButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionTab.ScriptsCheckButtonCtrl.IsEnabled = false;
            ConnectionTab.ScriptsCheckButtonCtrl.Content = "Checking...";

            try
            {
                int updatesFound = 0;

                // Check whatsapp-server.js
                var wwjsLocalVersion = GetLocalScriptVersion("whatsapp-server.js");
                var wwjsGithubVersion = await FetchGitHubScriptVersionAsync("whatsapp-server.js");

                if (wwjsGithubVersion != null && wwjsLocalVersion != wwjsGithubVersion)
                {
                    _latestWwjsScriptVersion = wwjsGithubVersion;
                    ConnectionTab.WwjsScriptUpdateButtonCtrl.Visibility = Visibility.Visible;
                    updatesFound++;
                }
                else
                {
                    ConnectionTab.WwjsScriptUpdateButtonCtrl.Visibility = Visibility.Collapsed;
                }

                // Check baileys-server.mjs
                var baileysLocalVersion = GetLocalScriptVersion("baileys-server.mjs");
                var baileysGithubVersion = await FetchGitHubScriptVersionAsync("baileys-server.mjs");

                if (baileysGithubVersion != null && baileysLocalVersion != baileysGithubVersion)
                {
                    _latestBaileysScriptVersion = baileysGithubVersion;
                    ConnectionTab.BaileysScriptUpdateButtonCtrl.Visibility = Visibility.Visible;
                    updatesFound++;
                }
                else
                {
                    ConnectionTab.BaileysScriptUpdateButtonCtrl.Visibility = Visibility.Collapsed;
                }

                // Check google-contacts.js
                var googleLocalVersion = GetLocalScriptVersion("google-contacts.js");
                var googleGithubVersion = await FetchGitHubScriptVersionAsync("google-contacts.js");

                if (googleGithubVersion != null && googleLocalVersion != googleGithubVersion)
                {
                    _latestGoogleScriptVersion = googleGithubVersion;
                    ConnectionTab.GoogleScriptUpdateButtonCtrl.Visibility = Visibility.Visible;
                    updatesFound++;
                }
                else
                {
                    ConnectionTab.GoogleScriptUpdateButtonCtrl.Visibility = Visibility.Collapsed;
                }

                // Show toast with result
                if (updatesFound > 0)
                {
                    ShowToast($"{updatesFound} script update(s) available", "🆕", 5);
                }
                else
                {
                    ShowToast("Scripts are up to date", "✅", 3);
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error checking scripts: {ex.Message}", "❌", 5);
            }
            finally
            {
                ConnectionTab.ScriptsCheckButtonCtrl.IsEnabled = true;
                ConnectionTab.ScriptsCheckButtonCtrl.Content = "Check for updates";
            }
        }

        /// <summary>
        /// Get local script version from a script file
        /// </summary>
        private string GetLocalScriptVersion(string scriptFileName)
        {
            try
            {
                var scriptPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node", scriptFileName);

                if (File.Exists(scriptPath))
                {
                    var content = File.ReadAllText(scriptPath);
                    var match = Regex.Match(content, @"SCRIPT_VERSION\s*=\s*[""']([^""']+)[""']");
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Fetch script version from GitHub repository
        /// </summary>
        private async Task<string> FetchGitHubScriptVersionAsync(string scriptFileName)
        {
            try
            {
                // Fetch raw file from GitHub
                var url = $"https://raw.githubusercontent.com/bfreis94/whatsapp-plugin/main/Resources/{scriptFileName}";
                var content = await _httpClient.GetStringAsync(url);

                var match = Regex.Match(content, @"SCRIPT_VERSION\s*=\s*[""']([^""']+)[""']");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[FetchGitHubScriptVersion] Error fetching {scriptFileName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Handle click on whatsapp-server.js update button
        /// </summary>
        private async void WwjsScriptUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_latestWwjsScriptVersion))
                return;

            var currentVersion = GetLocalScriptVersion("whatsapp-server.js") ?? "unknown";
            var confirmed = ConfirmDialog.Show(
                $"Update whatsapp-server.js to version {_latestWwjsScriptVersion}?",
                $"Current version: {currentVersion}",
                "Update Script",
                "Update",
                "Cancel");

            if (confirmed)
            {
                await UpdateScriptFromGitHubAsync("whatsapp-server.js", _latestWwjsScriptVersion);
                ConnectionTab.WwjsScriptUpdateButtonCtrl.Visibility = Visibility.Collapsed;
                ConnectionTab.WwjsScriptVersionTextCtrl.Text = _latestWwjsScriptVersion;
                _latestWwjsScriptVersion = null;
            }
        }

        /// <summary>
        /// Handle click on baileys-server.mjs update button
        /// </summary>
        private async void BaileysScriptUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_latestBaileysScriptVersion))
                return;

            var currentVersion = GetLocalScriptVersion("baileys-server.mjs") ?? "unknown";
            var confirmed = ConfirmDialog.Show(
                $"Update baileys-server.mjs to version {_latestBaileysScriptVersion}?",
                $"Current version: {currentVersion}",
                "Update Script",
                "Update",
                "Cancel");

            if (confirmed)
            {
                await UpdateScriptFromGitHubAsync("baileys-server.mjs", _latestBaileysScriptVersion);
                ConnectionTab.BaileysScriptUpdateButtonCtrl.Visibility = Visibility.Collapsed;
                ConnectionTab.BaileysScriptVersionTextCtrl.Text = _latestBaileysScriptVersion;
                _latestBaileysScriptVersion = null;
            }
        }

        /// <summary>
        /// Handle click on google-contacts.js update button
        /// </summary>
        private async void GoogleScriptUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_latestGoogleScriptVersion))
                return;

            var currentVersion = GetLocalScriptVersion("google-contacts.js") ?? "unknown";
            var confirmed = ConfirmDialog.Show(
                $"Update google-contacts.js to version {_latestGoogleScriptVersion}?",
                $"Current version: {currentVersion}",
                "Update Script",
                "Update",
                "Cancel");

            if (confirmed)
            {
                await UpdateScriptFromGitHubAsync("google-contacts.js", _latestGoogleScriptVersion);
                ConnectionTab.GoogleScriptUpdateButtonCtrl.Visibility = Visibility.Collapsed;
                ConnectionTab.GoogleScriptVersionTextCtrl.Text = _latestGoogleScriptVersion;
                _latestGoogleScriptVersion = null;
            }
        }

        /// <summary>
        /// Download and update a single script from GitHub
        /// </summary>
        private async Task UpdateScriptFromGitHubAsync(string scriptFileName, string newVersion)
        {
            try
            {
                ShowToast($"Downloading {scriptFileName}...", "📥", 5);

                var nodePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node");

                var url = $"https://raw.githubusercontent.com/bfreis94/whatsapp-plugin/main/Resources/{scriptFileName}";
                var content = await _httpClient.GetStringAsync(url);
                File.WriteAllText(Path.Combine(nodePath, scriptFileName), content);

                ShowToast($"{scriptFileName} updated to v{newVersion}!", "✅", 5);
            }
            catch (Exception ex)
            {
                ShowToast($"Error updating {scriptFileName}: {ex.Message}", "❌", 10);
            }
        }

        #endregion

        #region Dependencies Status Update Methods

        public void UpdateNodeStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    ConnectionTab.NodeJsStatusIconCtrl.Text = "❌";
                    ConnectionTab.NodeJsStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    ConnectionTab.NodeJsStatusIconCtrl.Text = "✓";
                    ConnectionTab.NodeJsStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                }
                else
                {
                    ConnectionTab.NodeJsStatusIconCtrl.Text = "⏳";
                    ConnectionTab.NodeJsStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                ConnectionTab.NodeJsStatusTextCtrl.Text = $"Node.js: {status}";
                UpdateDependenciesOverallStatus();
            });
        }

        public void UpdateGitStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    ConnectionTab.GitStatusIconCtrl.Text = "❌";
                    ConnectionTab.GitStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    ConnectionTab.GitStatusIconCtrl.Text = "✓";
                    ConnectionTab.GitStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                }
                else
                {
                    ConnectionTab.GitStatusIconCtrl.Text = "⏳";
                    ConnectionTab.GitStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                ConnectionTab.GitStatusTextCtrl.Text = $"Git: {status}";
                UpdateDependenciesOverallStatus();
            });
        }

        public void UpdateNpmStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    ConnectionTab.NpmPackagesStatusIconCtrl.Text = "❌";
                    ConnectionTab.NpmPackagesStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    ConnectionTab.NpmPackagesStatusIconCtrl.Text = "✓";
                    ConnectionTab.NpmPackagesStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                }
                else
                {
                    ConnectionTab.NpmPackagesStatusIconCtrl.Text = "⏳";
                    ConnectionTab.NpmPackagesStatusIconCtrl.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                ConnectionTab.NpmPackagesStatusTextCtrl.Text = $"Npm packages: {status}";
                UpdateDependenciesOverallStatus();
            });
        }

        /// <summary>
        /// Refresh scripts versions displayed in UI
        /// </summary>
        public void RefreshScriptsVersion()
        {
            Dispatcher.Invoke(() =>
            {
                var wwjsScriptVersion = GetLocalScriptVersion("whatsapp-server.js");
                ConnectionTab.WwjsScriptVersionTextCtrl.Text = wwjsScriptVersion ?? "N/A";

                var baileysScriptVersion = GetLocalScriptVersion("baileys-server.mjs");
                ConnectionTab.BaileysScriptVersionTextCtrl.Text = baileysScriptVersion ?? "N/A";

                var googleScriptVersion = GetLocalScriptVersion("google-contacts.js");
                ConnectionTab.GoogleScriptVersionTextCtrl.Text = googleScriptVersion ?? "N/A";
            });
        }

        public void SetDependenciesInstalling(bool isInstalling, string progressMessage = "")
        {
            Dispatcher.Invoke(() =>
            {
                if (isInstalling)
                {
                    ConnectionTab.DependenciesStatusTextCtrl.Text = "🔄 Installing dependencies...";
                    ConnectionTab.DependenciesStatusTextCtrl.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                    ConnectionTab.DependenciesProgressTextCtrl.Text = progressMessage;
                    ConnectionTab.DependenciesProgressTextCtrl.Visibility = Visibility.Visible;

                    // Disable connection buttons during installation
                    ConnectionTab.DisconnectButtonCtrl.IsEnabled = false;
                    ConnectionTab.ReconnectButtonCtrl.IsEnabled = false;
                    ConnectionTab.ResetSessionButtonCtrl.IsEnabled = false;
                }
                else
                {
                    ConnectionTab.DependenciesProgressTextCtrl.Visibility = Visibility.Collapsed;
                    UpdateDependenciesOverallStatus();

                    // Re-enable connection buttons after installation
                    // Check connection state to enable appropriate buttons
                    bool isConnected = ConnectionTab.StatusTextCtrl.Text == "Connected";
                    ConnectionTab.DisconnectButtonCtrl.IsEnabled = isConnected;
                    ConnectionTab.ReconnectButtonCtrl.IsEnabled = true;
                    ConnectionTab.ResetSessionButtonCtrl.IsEnabled = true;
                }
            });
        }

        private void UpdateDependenciesOverallStatus()
        {
            Dispatcher.Invoke(() =>
            {
                bool nodeOk = ConnectionTab.NodeJsStatusIconCtrl.Text == "✓";
                bool gitOk = ConnectionTab.GitStatusIconCtrl.Text == "✓";
                bool npmOk = ConnectionTab.NpmPackagesStatusIconCtrl.Text == "✓";

                bool anyError = ConnectionTab.NodeJsStatusIconCtrl.Text == "❌" ||
                                ConnectionTab.GitStatusIconCtrl.Text == "❌" ||
                                ConnectionTab.NpmPackagesStatusIconCtrl.Text == "❌";

                bool anyPending = ConnectionTab.NodeJsStatusIconCtrl.Text == "⏳" ||
                                  ConnectionTab.GitStatusIconCtrl.Text == "⏳" ||
                                  ConnectionTab.NpmPackagesStatusIconCtrl.Text == "⏳";

                if (anyError)
                {
                    ConnectionTab.DependenciesStatusTextCtrl.Text = "❌ Installation failed";
                    ConnectionTab.DependenciesStatusTextCtrl.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (anyPending)
                {
                    ConnectionTab.DependenciesStatusTextCtrl.Text = "⏳ Installing...";
                    ConnectionTab.DependenciesStatusTextCtrl.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                else if (nodeOk && gitOk && npmOk)
                {
                    ConnectionTab.DependenciesStatusTextCtrl.Text = "✅ All dependencies installed";
                    ConnectionTab.DependenciesStatusTextCtrl.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                }
            });
        }

        #endregion

        #region Plugin Auto-Update UI

        private bool _updateDownloaded = false;

        /// <summary>
        /// Handle Update button click (Download or Install)
        /// </summary>
        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateDownloaded)
            {
                // Install the update
                _plugin.InstallPluginUpdate();
            }
            else
            {
                // Download the update
                await _plugin.DownloadPluginUpdateAsync();
            }
        }

        /// <summary>
        /// Update the plugin update status text
        /// </summary>
        public void UpdatePluginUpdateStatus(string status, string color)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatusText.Text = status;
                UpdateStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            });
        }

        /// <summary>
        /// Show that a new version is available
        /// </summary>
        public void ShowPluginUpdateAvailable(string newVersion)
        {
            Dispatcher.Invoke(() =>
            {
                _updateDownloaded = false;

                UpdateArrowText.Visibility = Visibility.Visible;
                NewVersionText.Text = newVersion;
                NewVersionText.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "available";
                UpdateStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BCD4"));

                UpdateButton.Content = "Download";
                UpdateButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC"));
                UpdateButton.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Show that the update is ready to install
        /// </summary>
        public void ShowPluginUpdateReady(string newVersion)
        {
            Dispatcher.Invoke(() =>
            {
                _updateDownloaded = true;

                UpdateArrowText.Visibility = Visibility.Visible;
                NewVersionText.Text = newVersion;
                NewVersionText.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "ready";
                UpdateStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E7A0D"));

                UpdateButton.Content = "Install";
                UpdateButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E7A0D"));
                UpdateButton.Visibility = Visibility.Visible;
            });
        }

        #endregion

        /// <summary>
        /// Shows toast notification that disappears after 10 seconds
        /// </summary>
        public void ShowToast(string message, string icon = "ℹ️", int durationSeconds = 10)
        {
            Dispatcher.Invoke(() =>
            {
                ToastMessage.Text = message;
                ToastIcon.Text = icon;
                ToastNotification.Visibility = Visibility.Visible;

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(durationSeconds)
                };
                timer.Tick += (s, e) =>
                {
                    ToastNotification.Visibility = Visibility.Collapsed;
                    timer.Stop();
                };
                timer.Start();
            });
        }
    }
}
