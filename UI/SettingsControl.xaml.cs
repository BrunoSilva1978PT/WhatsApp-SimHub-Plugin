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
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using WhatsAppSimHubPlugin.Models;

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
        private DispatcherTimer _deviceRefreshTimer; // 🔥 Auto-refresh timer
        private DispatcherTimer _connectionStatusTimer; // 🔥 Timer para detectar crashes
        private bool _isLoadingDevices = false; // 🔥 Flag para evitar trigger durante loading
        private HashSet<string> _knownDeviceIds = new HashSet<string>(); // 🔥 Devices conhecidos
        private bool _lastConnectionState = false; // 🔥 Para detectar crashes
        private bool _userDisconnected = false; // 🔥 Flag para disconnect intencional
        private ObservableCollection<Contact> _chatContacts; // 📱 Contactos das conversas ativas

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

            // ✅ Criar ControlsEditor dinamicamente via reflexão
            CreateControlsEditors();

            // 🔍 EXPLORAR API DO SIMHUB PLUGINMANAGER
            ExploreSimHubAPI();

            InitializeData();
            LoadSettings();

            // 🔥 Iniciar timer de auto-refresh (a cada 5 segundos)
            _deviceRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _deviceRefreshTimer.Tick += (s, e) => LoadAvailableDevicesAsync();
            _deviceRefreshTimer.Start();

            // 🔥 Timer SIMPLIFICADO - apenas detectar crashes APÓS conectar
            _connectionStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) // A cada 5 segundos
            };
            _connectionStatusTimer.Tick += (s, e) => CheckScriptStatusPeriodic();
            _connectionStatusTimer.Start();
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
            ConnectionTab.WhatsAppWebJsUpdateBadgeCtrl.MouseLeftButtonDown += WhatsAppWebJsUpdateBadge_Click;

            // Baileys controls
            ConnectionTab.BaileysCheckButtonCtrl.Click += BaileysCheckButton_Click;
            ConnectionTab.BaileysInstallButtonCtrl.Click += BaileysInstallButton_Click;
            ConnectionTab.BaileysVersionComboCtrl.SelectionChanged += BaileysVersionCombo_SelectionChanged;
            ConnectionTab.BaileysOfficialRadioCtrl.Checked += BaileysSourceRadio_Changed;
            ConnectionTab.BaileysManualRadioCtrl.Checked += BaileysSourceRadio_Changed;
            ConnectionTab.BaileysApplyRepoButtonCtrl.Click += BaileysApplyRepo_Click;
            ConnectionTab.BaileysUpdateBadgeCtrl.MouseLeftButtonDown += BaileysUpdateBadge_Click;

            // Scripts controls
            ConnectionTab.ScriptsCheckButtonCtrl.Click += ScriptsCheckButton_Click;
            ConnectionTab.ScriptsUpdateBadgeCtrl.MouseLeftButtonDown += ScriptsUpdateBadge_Click;
        }

        /// <summary>
        /// Wire up event handlers for ContactsTab UserControl
        /// </summary>
        private void WireUpContactsTabEvents()
        {
            // Chat contacts
            ContactsTab.RefreshChatsButtonCtrl.Click += RefreshChatsButton_Click;
            ContactsTab.AddFromChatsButtonCtrl.Click += AddFromChatsButton_Click;

            // Manual add
            ContactsTab.AddManualButtonCtrl.Click += AddManualButton_Click;
        }

        /// <summary>
        /// Wire up event handlers for KeywordsTab UserControl
        /// </summary>
        private void WireUpKeywordsTabEvents()
        {
            KeywordsTab.AddKeywordButtonCtrl.Click += AddKeyword_Click;
            KeywordsTab.NewKeywordCtrl.KeyDown += NewKeyword_KeyDown;
        }

        /// <summary>
        /// Wire up event handlers for DisplayTab UserControl
        /// </summary>
        private void WireUpDisplayTabEvents()
        {
            DisplayTab.TargetDeviceComboBoxCtrl.SelectionChanged += TargetDeviceCombo_SelectionChanged;
            DisplayTab.RefreshDevicesButtonCtrl.Click += RefreshDevicesButton_Click;
            DisplayTab.TestOverlayButtonCtrl.Click += TestOverlayButton_Click;
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
        }

        private void LoadAvailableDevices()
        {
            try
            {
                // Obter VoCores CONECTADOS do plugin
                var devices = _plugin.GetAvailableDevices();

                // 🔥 OTIMIZAÇÃO: Só atualizar UI se houver NOVOS devices
                var currentDeviceIds = new HashSet<string>(devices.Select(d => d.Id));

                // Verificar se houve mudanças
                bool hasNewDevices = !currentDeviceIds.SetEquals(_knownDeviceIds);

                if (!hasNewDevices)
                {
                    // Nenhum device novo → Não fazer nada!
                    return;
                }

                // ✅ Há devices novos → Atualizar lista conhecida
                _knownDeviceIds = currentDeviceIds;

                _isLoadingDevices = true; // 🔥 Bloquear SelectionChanged

                // Limpar ComboBox
                DisplayTab.TargetDeviceComboBoxCtrl.Items.Clear();

                // 🔥 SEMPRE mostrar device salvo (mesmo se offline)
                if (!string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    // Verificar se device salvo está online
                    var savedDeviceOnline = devices.Any(d => d.Id == _settings.TargetDevice);

                    var savedItem = new ComboBoxItem
                    {
                        Content = savedDeviceOnline
                            ? $"{devices.First(d => d.Id == _settings.TargetDevice).Name} ✅"
                            : $"{_settings.TargetDevice} (Offline) ❌",
                        Tag = _settings.TargetDevice
                    };
                    DisplayTab.TargetDeviceComboBoxCtrl.Items.Add(savedItem);
                    DisplayTab.TargetDeviceComboBoxCtrl.SelectedIndex = 0;
                }

                // Adicionar outros VoCores online (se não forem o salvo)
                foreach (var device in devices.Where(d => d.Id != _settings.TargetDevice))
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{device.Name} ✅",
                        Tag = device.Id
                    };
                    DisplayTab.TargetDeviceComboBoxCtrl.Items.Add(item);
                }

                // Se não houver devices E não houver device salvo
                if (DisplayTab.TargetDeviceComboBoxCtrl.Items.Count == 0)
                {
                    var placeholder = new ComboBoxItem
                    {
                        Content = "No VoCore detected - connect and refresh",
                        IsEnabled = false
                    };
                    DisplayTab.TargetDeviceComboBoxCtrl.Items.Add(placeholder);
                }

                // 🔥 Atualizar status label
                UpdateDeviceStatus(devices.Count);
            }
            catch (Exception ex)
            {
                var errorItem = new ComboBoxItem
                {
                    Content = $"Error: {ex.Message}",
                    IsEnabled = false
                };
                DisplayTab.TargetDeviceComboBoxCtrl.Items.Clear();
                DisplayTab.TargetDeviceComboBoxCtrl.Items.Add(errorItem);
            }
            finally
            {
                _isLoadingDevices = false; // 🔥 Desbloquear SelectionChanged
            }
        }

        /// <summary>
        /// Async version that runs GetAvailableDevices in background thread to avoid UI freezing
        /// </summary>
        private async void LoadAvailableDevicesAsync()
        {
            try
            {
                // Run the reflection-heavy GetAvailableDevices on background thread
                var devices = await Task.Run(() => _plugin.GetAvailableDevices()).ConfigureAwait(false);
                var currentDeviceIds = new HashSet<string>(devices.Select(d => d.Id));

                // Check if there are changes before updating UI
                bool hasNewDevices = !currentDeviceIds.SetEquals(_knownDeviceIds);
                if (!hasNewDevices) return;

                // Update UI on dispatcher thread
                await Dispatcher.InvokeAsync(() =>
                {
                    _knownDeviceIds = currentDeviceIds;
                    UpdateDeviceListUI(devices);
                });
            }
            catch
            {
                // Silently ignore - timer will retry
            }
        }

        /// <summary>
        /// Helper method to update device list UI (must be called on UI thread)
        /// </summary>
        private void UpdateDeviceListUI(List<WhatsAppPlugin.DeviceInfo> devices)
        {
            _isLoadingDevices = true;
            try
            {
                DisplayTab.TargetDeviceComboBoxCtrl.Items.Clear();

                if (!string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    var savedDeviceOnline = devices.Any(d => d.Id == _settings.TargetDevice);
                    var savedItem = new ComboBoxItem
                    {
                        Content = savedDeviceOnline
                            ? $"{devices.First(d => d.Id == _settings.TargetDevice).Name} ✅"
                            : $"{_settings.TargetDevice} (Offline) ❌",
                        Tag = _settings.TargetDevice
                    };
                    DisplayTab.TargetDeviceComboBoxCtrl.Items.Add(savedItem);
                    DisplayTab.TargetDeviceComboBoxCtrl.SelectedIndex = 0;
                }

                foreach (var device in devices.Where(d => d.Id != _settings.TargetDevice))
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{device.Name} ✅",
                        Tag = device.Id
                    };
                    DisplayTab.TargetDeviceComboBoxCtrl.Items.Add(item);
                }

                if (DisplayTab.TargetDeviceComboBoxCtrl.Items.Count == 0)
                {
                    var placeholder = new ComboBoxItem
                    {
                        Content = "No VoCore detected - connect and refresh",
                        IsEnabled = false
                    };
                    DisplayTab.TargetDeviceComboBoxCtrl.Items.Add(placeholder);
                }

                UpdateDeviceStatus(devices.Count);
            }
            finally
            {
                _isLoadingDevices = false;
            }
        }

        private void UpdateDeviceStatus(int connectedCount)
        {
            // Atualizar label de status ao lado do Refresh
            Dispatcher.Invoke(() =>
            {
                if (DisplayTab.DeviceStatusLabelCtrl != null)
                {
                    if (connectedCount > 0)
                    {
                        DisplayTab.DeviceStatusLabelCtrl.Text = $"✅ {connectedCount} VoCore(s) connected";
                        DisplayTab.DeviceStatusLabelCtrl.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                    }
                    else
                    {
                        DisplayTab.DeviceStatusLabelCtrl.Text = "❌ No VoCores detected";
                        DisplayTab.DeviceStatusLabelCtrl.Foreground = new SolidColorBrush(Color.FromRgb(231, 72, 119));
                    }
                }
            });
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

                // 🔧 Carregar Target Device (se salvo)
                if (!string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    foreach (ComboBoxItem item in DisplayTab.TargetDeviceComboBoxCtrl.Items)
                    {
                        if (item.Tag?.ToString() == _settings.TargetDevice)
                        {
                            DisplayTab.TargetDeviceComboBoxCtrl.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Sliders - converter de ms para segundos onde necessário
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

                // Atualizar UI da tab Contacts baseado no backend
                UpdateContactsTabForBackend();

            }
            catch (Exception ex)
            {
                ShowToast($"Error loading settings: {ex.Message}", "❌", 10);
            }

            // Verificar se o script Node.js está a correr
            CheckScriptStatus();

            // Load backend library settings
            LoadBackendLibrarySettings();
        }

        private void CheckScriptStatus()
        {
            // No início, verificar se Node.js está instalado
            // Se não está, mostrar erro permanente
            // Se está, mostrar "Disconnected" (estado inicial normal)

            if (!_plugin.IsNodeJsInstalled())
            {
                UpdateConnectionStatus("Node.js not installed");
            }
            else
            {
                // Node.js instalado, mostrar estado inicial "Disconnected"
                UpdateConnectionStatus("Disconnected");
            }
        }

        private void CheckScriptStatusPeriodic()
        {
            // 🔥 TIMER SIMPLIFICADO:
            // Apenas verifica se Node.js está instalado!
            // TODOS os outros estados são controlados por eventos/botões!

            // Se user desconectou, respeitar estado "Disconnected"
            if (_userDisconnected)
            {
                return;
            }

            var currentStatus = ConnectionTab.StatusTextCtrl.Text.ToLower();

            // Se já mostra erro de Node.js, não verificar de novo
            if (currentStatus.Contains("node.js"))
            {
                return;
            }

            // Estados válidos que o timer NÃO deve tocar:
            // - "connected" → Tudo bem!
            // - "connecting" → Processo em curso
            // - "qr" → Aguardando scan
            // - "disconnected" → Estado normal desligado

            // Timer deixa TUDO em paz! Eventos controlam os estados!
            // Única exceção: verificar se Node.js crashou APÓS estar conectado

            bool currentState = _plugin.IsScriptRunning;

            // Se estava conectado E agora script não está a correr → Crash!
            if (_lastConnectionState && !currentState && currentStatus == "connected")
            {
                // Script crashou após estar conectado
                UpdateConnectionStatus("Disconnected");
                _userDisconnected = true; // Marcar como desconectado
            }

            _lastConnectionState = currentState;
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

                        // ✅ ESCONDER QR CODE quando conecta
                        ConnectionTab.QRCodeImageCtrl.Visibility = Visibility.Collapsed;
                        ConnectionTab.QRCodeInstructionsCtrl.Visibility = Visibility.Collapsed;
                        break;

                    case "connecting":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Disconnect deve estar sempre disponível
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = true;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 1.0;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 0.5;
                        ConnectionTab.ConnectedNumberTextCtrl.Text = "Connecting to WhatsApp...";
                        break;

                    case "qr":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Disconnect deve estar sempre disponível
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
                    case "error":
                        ConnectionTab.StatusIndicatorCtrl.Fill = new SolidColorBrush(Color.FromRgb(231, 72, 119)); // Red

                        // Disconnect disabled, Reconnect enabled
                        ConnectionTab.DisconnectButtonCtrl.IsEnabled = false;
                        ConnectionTab.DisconnectButtonCtrl.Opacity = 0.5;
                        ConnectionTab.ReconnectButtonCtrl.IsEnabled = true;
                        ConnectionTab.ReconnectButtonCtrl.Opacity = 1.0;

                        ConnectionTab.ConnectedNumberTextCtrl.Text = "No connection";
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
                _userDisconnected = true; // 🔥 Marcar que user desconectou intencionalmente

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

        private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Limpar cache para forçar atualização completa
                _knownDeviceIds.Clear();
                LoadAvailableDevices();
                ShowToast("Devices refreshed! VoCores should now appear if connected.", "✅", 5);
            }
            catch (Exception ex)
            {
                ShowToast($"Error refreshing devices: {ex.Message}", "❌", 10);
            }
        }

        private async void TestOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Verificar se há device selecionado
                if (string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    ShowToast("Please select a VoCore device first!", "⚠️", 5);
                    return;
                }

                // Desactivar botão durante o teste
                DisplayTab.TestOverlayButtonCtrl.IsEnabled = false;

                // ✅ NOVO TESTE: Não muda VoCore, não muda dashboard, só mostra mensagem
                _plugin.ShowTestMessage();

                ShowToast("Testing selected VoCore for 5s", "✅", 5);

                // Reactivar botão após 5 segundos
                await Task.Delay(5000);
                DisplayTab.TestOverlayButtonCtrl.IsEnabled = true;
            }
            catch (Exception ex)
            {
                DisplayTab.TestOverlayButtonCtrl.IsEnabled = true; // Garantir que reactiva em caso de erro
                ShowToast($"Error testing overlay: {ex.Message}", "❌", 10);
            }
        }

        /// <summary>
        /// Salvar display settings sem UI feedback
        /// </summary>
        private void SaveDisplaySettingsInternal()
        {
            try
            {
                // Salvar device selecionado
                if (DisplayTab.TargetDeviceComboBoxCtrl.SelectedItem is ComboBoxItem selectedItem)
                {
                    var deviceId = selectedItem.Tag?.ToString();
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        _settings.TargetDevice = deviceId;
                    }
                }

                _plugin.SaveSettings();
            }
            catch (Exception ex)
            {
                // Silent fail
                System.Diagnostics.Debug.WriteLine($"SaveDisplaySettingsInternal error: {ex.Message}");
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
                _userDisconnected = false; // 🔥 Limpar flag - user quer conectar novamente

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
                var result = System.Windows.MessageBox.Show(
                    "This will delete your saved WhatsApp session and you will need to scan the QR code again.\n\nContinue?",
                    "Reset Session",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes)
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
                    // Para Baileys, apagar toda a pasta data_baileys (contém auth_info e store)
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

        /// <summary>
        /// Atualizar lista de contactos das conversas (chamado pelo plugin)
        /// </summary>
        public void UpdateChatContactsList(ObservableCollection<Contact> contacts)
        {
            Dispatcher.Invoke(() =>
            {
                _chatContacts = contacts;
                ContactsTab.ChatContactsComboBoxCtrl.ItemsSource = _chatContacts;

                if (contacts != null && contacts.Count > 0)
                {
                    ContactsTab.ChatContactsComboBoxCtrl.IsEnabled = true;
                    ContactsTab.AddFromChatsButtonCtrl.IsEnabled = true;
                    ContactsTab.RefreshChatsButtonCtrl.IsEnabled = true;
                    ContactsTab.ChatsStatusTextCtrl.Text = $"✅ {contacts.Count} contacts from active chats";
                    ContactsTab.ChatsStatusTextCtrl.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                else
                {
                    ContactsTab.ChatContactsComboBoxCtrl.IsEnabled = false;
                    ContactsTab.AddFromChatsButtonCtrl.IsEnabled = false;
                    ContactsTab.RefreshChatsButtonCtrl.IsEnabled = false;
                    ContactsTab.ChatsStatusTextCtrl.Text = "⚠️ No active chats found";
                    ContactsTab.ChatsStatusTextCtrl.Foreground = System.Windows.Media.Brushes.Orange;
                }
            });
        }

        /// <summary>
        /// Adicionar contacto das conversas à lista de allowed
        /// </summary>
        private void AddFromChatsButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ContactsTab.ChatContactsComboBoxCtrl.SelectedItem as Contact;

            if (selected == null)
            {
                ShowToast("Please select a contact from the list.", "⚠️", 5);
                return;
            }

            // Verificar se já existe
            var existing = _contacts.FirstOrDefault(c =>
                c.Number.Replace("+", "").Replace(" ", "").Replace("-", "") ==
                selected.Number.Replace("+", "").Replace(" ", "").Replace("-", ""));

            if (existing != null)
            {
                ShowToast($"{existing.Name} is already in your allowed contacts list.", "ℹ️", 5);
                return;
            }

            // Adicionar novo contacto
            var newContact = new Contact
            {
                Name = selected.Name,
                Number = selected.Number,
                IsVip = false  // Por defeito não é VIP
            };

            _contacts.Add(newContact);

            ContactsTab.ChatContactsComboBoxCtrl.SelectedIndex = -1;

            ShowToast($"{newContact.Name} added to allowed contacts!", "✅");
        }

        /// <summary>
        /// Refresh lista de contactos das conversas
        /// </summary>
        private void RefreshChatsButton_Click(object sender, RoutedEventArgs e)
        {
            // Atualizar UI para mostrar que está a refreshar
            ContactsTab.ChatsStatusTextCtrl.Text = "🔄 Refreshing contacts...";
            ContactsTab.ChatsStatusTextCtrl.Foreground = System.Windows.Media.Brushes.Orange;

            ContactsTab.RefreshChatsButtonCtrl.IsEnabled = false;

            // Pedir ao plugin para refresh
            _plugin.RefreshChatContacts();

            // O botão será reativado quando UpdateChatContactsList() for chamado
        }

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

        private void NewKeyword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddKeyword_Click(sender, e);
            }
        }

        #endregion

        #region Display Tab

        private void TargetDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 🔥 Ignorar se está carregando devices (evita trigger a cada 5s)
            if (_isLoadingDevices) return;

            // Null check: evento pode disparar antes de _plugin/_settings serem inicializados
            if (_plugin == null || _settings == null) return;

            if (DisplayTab.TargetDeviceComboBoxCtrl.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string newDevice = item.Tag.ToString();

                // 🔥 Só reattach se device REALMENTE mudou
                if (_settings.TargetDevice != newDevice)
                {
                    _settings.TargetDevice = newDevice;
                    _plugin.SaveSettings();
                    _plugin.ApplyDisplaySettings();

                    // 🎯 AUTO-ATIVAR OVERLAY quando device muda
                    _plugin.ReattachAndActivateOverlay();
                }
            }
        }


        #endregion

        #region Quick Replies Tab


        /// <summary>
        /// 🔍 DEBUG: Descobrir todos os métodos disponíveis no PluginManager
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

                // ======= MÉTODOS =======
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

                // ======= TIPOS DISPONÍVEIS NA ASSEMBLY =======
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
                    .Take(50)  // Limitar a 50 para não ficar muito grande
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
        /// Adicionar contacto manualmente
        /// </summary>
        private void AddManualButton_Click(object sender, RoutedEventArgs e)
        {
            string name = ContactsTab.ManualNameTextBoxCtrl.Text.Trim();
            string number = ContactsTab.ManualNumberTextBoxCtrl.Text.Trim();

            // Validar
            if (string.IsNullOrWhiteSpace(name) || name == "Name")
            {
                ShowToast("Please enter a name.", "⚠️", 5);
                return;
            }

            if (string.IsNullOrWhiteSpace(number) || number == "+351..." || !number.StartsWith("+"))
            {
                ShowToast("Please enter a valid phone number.\n\nFormat: +[country code][number]\nExample: +351912345678", "⚠️", 8);
                return;
            }

            // Verificar duplicado
            var existing = _contacts.FirstOrDefault(c =>
                c.Number.Replace("+", "").Replace(" ", "").Replace("-", "") ==
                number.Replace("+", "").Replace(" ", "").Replace("-", ""));

            if (existing != null)
            {
                ShowToast("A contact with this number already exists.", "ℹ️", 5);
                return;
            }

            // Adicionar
            var contact = new Contact
            {
                Name = name,
                Number = number,
                IsVip = false  // Por defeito não é VIP
            };

            _contacts.Add(contact);
            _plugin.SaveSettings();

            // Limpar
            ContactsTab.ManualNameTextBoxCtrl.Text = "Name";
            ContactsTab.ManualNumberTextBoxCtrl.Text = "+351...";

            ShowToast($"{contact.Name} added to allowed contacts!", "✅", 5);
        }

        /// <summary>
        /// Remover contacto
        /// </summary>
        private void RemoveContactButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var contact = button?.Tag as Contact;

            if (contact == null) return;

            var result = MessageBox.Show($"Remove {contact.Name} from allowed contacts?\n\nThey will no longer be able to send you messages.",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
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
            if (_settings.BackendMode == newMode) return; // Sem alteração

            _settings.BackendMode = newMode;
            _plugin.SaveSettings();

            // Atualizar UI da tab Contacts
            UpdateContactsTabForBackend();

            // Fazer switch automático do backend
            try
            {
                // Mostrar mensagem que vai fazer reconnect
                ShowToast($"Switching to {selected.Content}...", "🔄", 3);

                // Fazer switch do backend (para, aguarda, cria novo, inicia)
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
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = new JObject { ["enabled"] = enabled }.ToString();
                File.WriteAllText(path, json);
            }
            catch { }
        }

        /// <summary>
        /// Checkbox VIP changed
        /// </summary>
        private void ContactVipCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Guardar automaticamente quando checkbox muda
            _plugin.SaveSettings();
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
        /// 🔍 EXPLORAR API DO SIMHUB - Descobrir métodos disponíveis
        /// </summary>
        /// <summary>
        /// ✅ Criar ControlsEditor dinamicamente via reflexão
        /// Isto evita erros de compilação se o tipo não existir
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

                // Criar instância para Reply1
                var reply1Editor = Activator.CreateInstance(controlsEditorType);
                if (reply1Editor != null)
                {
                    // Configurar propriedades
                    // ⚡ CRÍTICO: ControlsEditor NÃO adiciona prefixo automaticamente!
                    // Temos que usar o nome COMPLETO: "WhatsAppPlugin.SendReply1"
                    controlsEditorType.GetProperty("ActionName")?.SetValue(reply1Editor, "WhatsAppPlugin.SendReply1");

                    // ✅ Substituir conteúdo do ContentPresenter
                    if (QuickRepliesTab.Reply1ControlEditorPlaceholderCtrl != null)
                    {
                        QuickRepliesTab.Reply1ControlEditorPlaceholderCtrl.Content = reply1Editor;
                    }

                    WriteDebugLog("[ControlsEditor] Reply1 editor created successfully");
                }

                // Criar instância para Reply2
                var reply2Editor = Activator.CreateInstance(controlsEditorType);
                if (reply2Editor != null)
                {
                    // Configurar propriedades
                    // ⚡ CRÍTICO: ControlsEditor NÃO adiciona prefixo automaticamente!
                    // Temos que usar o nome COMPLETO: "WhatsAppPlugin.SendReply2"
                    controlsEditorType.GetProperty("ActionName")?.SetValue(reply2Editor, "WhatsAppPlugin.SendReply2");

                    // ✅ Substituir conteúdo do ContentPresenter
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
                // Não fazer nada - deixar os placeholders mostrarem mensagem padrão
            }
        }

        private void WriteDebugLog(string message)
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "logs", "ui-debug.log");

                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private void ExploreSimHubAPI()
        {
            try
            {
                if (_plugin?.PluginManager == null)
                {
                    System.Diagnostics.Debug.WriteLine("[API EXPLORER] PluginManager is null");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("╔══════════════════════════════════════════╗");
                System.Diagnostics.Debug.WriteLine("║   🔍 SIMHUB PLUGINMANAGER API EXPLORER   ║");
                System.Diagnostics.Debug.WriteLine("╚══════════════════════════════════════════╝");

                var type = _plugin.PluginManager.GetType();
                var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                System.Diagnostics.Debug.WriteLine("\n📋 MÉTODOS RELACIONADOS COM INPUT/BUTTON/CONTROL:");
                System.Diagnostics.Debug.WriteLine("════════════════════════════════════════════════\n");

                int count = 0;
                foreach (var method in methods)
                {
                    var name = method.Name.ToLower();
                    if (name.Contains("input") || name.Contains("button") ||
                        name.Contains("control") || name.Contains("picker") ||
                        name.Contains("bind") || name.Contains("configure") ||
                        name.Contains("select") || name.Contains("choose") ||
                        name.Contains("action"))
                    {
                        count++;
                        System.Diagnostics.Debug.WriteLine($"✅ {count}. {method.Name}");

                        var parameters = method.GetParameters();
                        if (parameters.Length > 0)
                        {
                            System.Diagnostics.Debug.WriteLine("   Parâmetros:");
                            foreach (var param in parameters)
                            {
                                System.Diagnostics.Debug.WriteLine($"     • {param.ParameterType.Name} {param.Name}");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("   (Sem parâmetros)");
                        }

                        System.Diagnostics.Debug.WriteLine($"   Retorno: {method.ReturnType.Name}");
                        System.Diagnostics.Debug.WriteLine("");
                    }
                }

                if (count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Nenhum método encontrado com esses padrões.");
                }

                System.Diagnostics.Debug.WriteLine("\n════════════════════════════════════════════════");
                System.Diagnostics.Debug.WriteLine($"Total de métodos encontrados: {count}");
                System.Diagnostics.Debug.WriteLine("════════════════════════════════════════════════\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API EXPLORER ERROR] {ex.Message}");
            }
        }

        /// <summary>
        /// Atualizar UI da tab Contacts baseado no backend ativo
        /// </summary>
        private void UpdateContactsTabForBackend()
        {
            bool isBaileys = _settings.BackendMode == "baileys";

            // Desativar funcionalidade de chats se for Baileys
            ContactsTab.ChatContactsComboBoxCtrl.IsEnabled = !isBaileys;
            ContactsTab.RefreshChatsButtonCtrl.IsEnabled = !isBaileys;
            ContactsTab.AddFromChatsButtonCtrl.IsEnabled = !isBaileys;

            // Atualizar texto de status
            if (isBaileys)
            {
                ContactsTab.ChatsStatusTextCtrl.Text = "⚠️ Chat contacts list is not supported with Baileys backend. Please use WhatsApp-Web.js.";
                ContactsTab.ChatsStatusTextCtrl.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange
            }
            else
            {
                // Restaurar texto original (será atualizado quando carregar contactos)
                ContactsTab.ChatsStatusTextCtrl.Text = "Click Refresh to load contacts from active chats";
                ContactsTab.ChatsStatusTextCtrl.Foreground = new SolidColorBrush(Color.FromRgb(133, 133, 133)); // Gray
            }
        }

        #region Backend Libraries Management

        private static readonly HttpClient _httpClient = new HttpClient();
        private List<string> _whatsappWebJsVersions = new List<string>();
        private List<string> _baileysVersions = new List<string>();
        private string _latestScriptVersion = null;

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
            }
            else
            {
                ConnectionTab.BaileysOfficialRadioCtrl.IsChecked = true;
                ConnectionTab.BaileysManualPanelCtrl.Visibility = Visibility.Collapsed;
            }

            // Load current installed versions
            LoadInstalledVersions();
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

                            // Se for github:main, mostrar de forma amigável
                            if (wwjsSpec.Contains("github:") && wwjsSpec.Contains("#main"))
                            {
                                displayVersion = "github:main (latest dev)";
                                tagVersion = "github:pedroslopez/whatsapp-web.js#main";
                            }
                            // Se for versão normal, remover o ^ se existir
                            else if (wwjsSpec.StartsWith("^"))
                            {
                                displayVersion = wwjsSpec.Substring(1);
                                tagVersion = wwjsSpec.Substring(1);
                            }

                            _settings.WhatsAppWebJsVersion = tagVersion;

                            // Adicionar opção github:main
                            var mainItem = new ComboBoxItem
                            {
                                Content = "github:main (latest dev)",
                                Tag = "github:pedroslopez/whatsapp-web.js#main"
                            };
                            ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Add(mainItem);
                            if (tagVersion.Contains("#main")) mainItem.IsSelected = true;

                            // Adicionar versão instalada se não for github:main
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
                            // Se for versão normal, remover npm: e ^ se existir
                            else
                            {
                                displayVersion = baileysSpec.Replace("npm:@whiskeysockets/baileys@", "").Replace("^", "");
                                tagVersion = displayVersion;
                            }

                            _settings.BaileysVersion = tagVersion;

                            // Adicionar opção @latest
                            var latestItem = new ComboBoxItem
                            {
                                Content = "@latest (latest version)",
                                Tag = "npm:@whiskeysockets/baileys@latest"
                            };
                            ConnectionTab.BaileysVersionComboCtrl.Items.Add(latestItem);
                            if (tagVersion.Contains("@latest")) latestItem.IsSelected = true;

                            // Adicionar versão instalada se não for @latest
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

                // Scripts version
                var scriptsVersion = GetLocalScriptVersion();
                ConnectionTab.ScriptsVersionTextCtrl.Text = scriptsVersion ?? "Not installed";
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

                    // Adicionar versões do npm (últimas 10 v7.x) que ainda não existem
                    foreach (var version in versions.Take(10))
                    {
                        // Verificar se já existe no dropdown
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

                    // Restaurar seleção
                    if (currentlySelected != null)
                    {
                        ConnectionTab.WhatsAppWebJsVersionComboCtrl.SelectedItem = currentlySelected;
                    }

                    // Verificar se há update disponível
                    // Comparar apenas se a versão instalada for numérica (não github:main)
                    bool hasUpdate = false;
                    if (!currentVersion.Contains("github:") && !currentVersion.Contains("#main"))
                    {
                        var installedVersionClean = currentVersion.Replace("^", "").Replace("npm:", "");
                        hasUpdate = versions.Count > 0 && versions[0] != installedVersionClean;
                    }

                    if (hasUpdate)
                    {
                        ConnectionTab.WhatsAppWebJsUpdateBadgeCtrl.Visibility = Visibility.Visible;
                        ShowToast($"New whatsapp-web.js version available: {versions[0]}", "🆕", 5);
                    }
                    else
                    {
                        ConnectionTab.WhatsAppWebJsUpdateBadgeCtrl.Visibility = Visibility.Collapsed;
                        ShowToast("whatsapp-web.js versions loaded!", "✅", 3);
                    }
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
                var allVersions = await FetchNpmVersionsAsync("@whiskeysockets/baileys");

                // Filter only v7.x versions (script requires Baileys v7)
                var versions = allVersions.Where(v => v.StartsWith("7.")).ToList();

                if (versions.Count > 0)
                {
                    _baileysVersions = versions;
                    var currentVersion = _settings.BaileysVersion;

                    // Guardar item selecionado atual
                    var currentlySelectedTag = (ConnectionTab.BaileysVersionComboCtrl.SelectedItem as ComboBoxItem)?.Tag?.ToString();

                    // Limpar e reconstruir dropdown com versões ordenadas
                    ConnectionTab.BaileysVersionComboCtrl.Items.Clear();

                    // 1. Adicionar @latest primeiro
                    var latestItem = new ComboBoxItem
                    {
                        Content = "@latest (latest version)",
                        Tag = "npm:@whiskeysockets/baileys@latest"
                    };
                    ConnectionTab.BaileysVersionComboCtrl.Items.Add(latestItem);

                    // 2. Adicionar versões v7.x do npm (últimas 10)
                    foreach (var version in versions.Take(10))
                    {
                        var item = new ComboBoxItem
                        {
                            Content = version,
                            Tag = version
                        };
                        ConnectionTab.BaileysVersionComboCtrl.Items.Add(item);
                    }

                    // 3. Restaurar seleção
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

                    // Verificar se há update disponível
                    // Comparar apenas se a versão instalada for numérica (não @latest)
                    bool hasUpdate = false;
                    if (!currentVersion.Contains("@latest"))
                    {
                        var installedVersionClean = currentVersion.Replace("^", "").Replace("npm:@whiskeysockets/baileys@", "");
                        hasUpdate = versions.Count > 0 && versions[0] != installedVersionClean;
                    }

                    if (hasUpdate)
                    {
                        ConnectionTab.BaileysUpdateBadgeCtrl.Visibility = Visibility.Visible;
                        ShowToast($"New baileys version available: {versions[0]}", "🆕", 5);
                    }
                    else
                    {
                        ConnectionTab.BaileysUpdateBadgeCtrl.Visibility = Visibility.Collapsed;
                        ShowToast("baileys versions loaded!", "✅", 3);
                    }
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
                            ConnectionTab.WhatsAppWebJsUpdateBadgeCtrl.Visibility = Visibility.Collapsed;
                        }
                        else if (packageName.Contains("baileys"))
                        {
                            _settings.BaileysVersion = version;
                            ConnectionTab.BaileysUpdateBadgeCtrl.Visibility = Visibility.Collapsed;
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

                var result = MessageBox.Show(
                    $"Install whatsapp-web.js {selectedVersion}?\n\n" +
                    $"The plugin will:\n" +
                    $"1. Disconnect from WhatsApp\n" +
                    $"2. Stop all Node.js and Chrome processes\n" +
                    $"3. Delete node_modules and reinstall\n" +
                    $"4. Reconnect automatically when done\n\n" +
                    $"This may take 1-2 minutes. Continue?",
                    "Install Library",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
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

                var result = MessageBox.Show(
                    $"Install baileys {selectedVersion}?\n\n" +
                    $"The plugin will:\n" +
                    $"1. Disconnect from WhatsApp\n" +
                    $"2. Stop all Node.js and Chrome processes\n" +
                    $"3. Delete node_modules and reinstall\n" +
                    $"4. Reconnect automatically when done\n\n" +
                    $"This may take 1-2 minutes. Continue?",
                    "Install Library",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    InstallLibraryInBackground("@whiskeysockets/baileys", selectedVersion);
                }
            }
        }

        /// <summary>
        /// Install library in background WITHOUT restarting SimHub
        /// </summary>
        private async void InstallLibraryInBackground(string packageName, string version)
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
                            }
                            else if (packageName.Contains("baileys"))
                            {
                                _settings.BaileysVersion = version;
                                ConnectionTab.BaileysInstallButtonCtrl.Visibility = Visibility.Collapsed;
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
            }
            else
            {
                ConnectionTab.WhatsAppWebJsManualPanelCtrl.Visibility = Visibility.Collapsed;
                _settings.WhatsAppWebJsSource = "official";

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
            }
            else
            {
                ConnectionTab.BaileysManualPanelCtrl.Visibility = Visibility.Collapsed;
                _settings.BaileysSource = "official";

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
                ShowToast("Please enter a repository (e.g., user/repo#branch)", "⚠️", 5);
                return;
            }

            var result = MessageBox.Show(
                $"Install whatsapp-web.js from GitHub repository?\n\nRepository: {repo}\n\nThis will:\n• Stop WhatsApp connection\n• Run npm install\n• Restart connection\n\nContinue?",
                "Install from Repository",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _settings.WhatsAppWebJsManualRepo = repo;
                _plugin.SaveSettings();

                // Install from GitHub
                await InstallFromGitHubAsync("whatsapp-web.js", repo);
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
                ShowToast("Please enter a repository (e.g., user/repo#branch)", "⚠️", 5);
                return;
            }

            var result = MessageBox.Show(
                $"Install baileys from GitHub repository?\n\nRepository: {repo}\n\nThis will:\n• Stop WhatsApp connection\n• Run npm install\n• Restart connection\n\nContinue?",
                "Install from Repository",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _settings.BaileysManualRepo = repo;
                _plugin.SaveSettings();

                // Install from GitHub
                await InstallFromGitHubAsync("@whiskeysockets/baileys", repo);
            }
        }

        /// <summary>
        /// Install library from GitHub repository
        /// </summary>
        private async Task InstallFromGitHubAsync(string packageName, string repo)
        {
            try
            {
                ShowToast($"Installing {packageName} from {repo}...", "📦", 10);

                // Stop current connection gracefully
                _plugin.DisconnectWhatsApp();
                await Task.Delay(2000); // Wait for graceful shutdown

                // Get node folder path
                var nodePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node");

                // Format: npm install github:user/repo#branch
                var npmPackage = $"github:{repo}";

                // Use cmd.exe /c because npm is a batch script on Windows
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c npm install {npmPackage}",
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
                        ShowToast($"{packageName} installed from {repo}!", "✅", 5);
                        LoadInstalledVersions();
                    }
                    else
                    {
                        ShowToast($"npm install failed: {error}", "❌", 10);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error installing from repository: {ex.Message}", "❌", 10);
            }
        }

        /// <summary>
        /// Handle click on whatsapp-web.js update badge
        /// </summary>
        private void WhatsAppWebJsUpdateBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_whatsappWebJsVersions.Count > 0)
            {
                // Select the latest version in combo
                if (ConnectionTab.WhatsAppWebJsVersionComboCtrl.Items.Count > 0)
                {
                    ConnectionTab.WhatsAppWebJsVersionComboCtrl.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Handle click on baileys update badge
        /// </summary>
        private void BaileysUpdateBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_baileysVersions.Count > 0)
            {
                // Select the latest version in combo
                if (ConnectionTab.BaileysVersionComboCtrl.Items.Count > 0)
                {
                    ConnectionTab.BaileysVersionComboCtrl.SelectedIndex = 0;
                }
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
                // Get local script version
                var localVersion = GetLocalScriptVersion();

                // Get GitHub version
                var githubVersion = await FetchGitHubScriptVersionAsync();

                if (githubVersion != null && localVersion != githubVersion)
                {
                    _latestScriptVersion = githubVersion;
                    ConnectionTab.ScriptsUpdateBadgeCtrl.Visibility = Visibility.Visible;
                    ShowToast($"New scripts version available: {githubVersion} (current: {localVersion})", "🆕", 5);
                }
                else
                {
                    ConnectionTab.ScriptsUpdateBadgeCtrl.Visibility = Visibility.Collapsed;
                    ShowToast($"Scripts are up to date (v{localVersion})", "✅", 3);
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
        /// Get local script version from whatsapp-server.js
        /// </summary>
        private string GetLocalScriptVersion()
        {
            try
            {
                var scriptPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node", "whatsapp-server.js");

                if (File.Exists(scriptPath))
                {
                    var content = File.ReadAllText(scriptPath);
                    var match = Regex.Match(content, @"SCRIPT_VERSION\s*=\s*[""']([^""']+)[""']");
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            catch { }

            return "1.0.0";
        }

        /// <summary>
        /// Fetch script version from GitHub repository
        /// </summary>
        private async Task<string> FetchGitHubScriptVersionAsync()
        {
            try
            {
                // Fetch raw file from GitHub
                var url = "https://raw.githubusercontent.com/bfreis94/whatsapp-plugin/main/Resources/whatsapp-server.js";
                var content = await _httpClient.GetStringAsync(url);

                var match = Regex.Match(content, @"SCRIPT_VERSION\s*=\s*[""']([^""']+)[""']");
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[FetchGitHubScriptVersion] Error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Handle click on scripts update badge - download and apply updates
        /// </summary>
        private async void ScriptsUpdateBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_latestScriptVersion))
                return;

            var result = MessageBox.Show(
                $"Update scripts to version {_latestScriptVersion}?\n\nThis will:\n• Download latest scripts from GitHub\n• Replace local scripts\n• Restart connection\n\nContinue?",
                "Update Scripts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await UpdateScriptsFromGitHubAsync();
            }
        }

        /// <summary>
        /// Download and update scripts from GitHub
        /// </summary>
        private async Task UpdateScriptsFromGitHubAsync()
        {
            try
            {
                ShowToast("Downloading latest scripts...", "📥", 10);

                // Stop current connection gracefully
                _plugin.DisconnectWhatsApp();
                await Task.Delay(2000); // Wait for graceful shutdown

                var nodePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node");

                // Download whatsapp-server.js
                var wwjsContent = await _httpClient.GetStringAsync(
                    "https://raw.githubusercontent.com/bfreis94/whatsapp-plugin/main/Resources/whatsapp-server.js");
                File.WriteAllText(Path.Combine(nodePath, "whatsapp-server.js"), wwjsContent);

                // Download baileys-server.mjs
                var baileysContent = await _httpClient.GetStringAsync(
                    "https://raw.githubusercontent.com/bfreis94/whatsapp-plugin/main/Resources/baileys-server.mjs");
                File.WriteAllText(Path.Combine(nodePath, "baileys-server.mjs"), baileysContent);

                ConnectionTab.ScriptsUpdateBadgeCtrl.Visibility = Visibility.Collapsed;
                ShowToast($"Scripts updated to v{_latestScriptVersion}!", "✅", 5);
            }
            catch (Exception ex)
            {
                ShowToast($"Error updating scripts: {ex.Message}", "❌", 10);
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
        /// Refresh scripts version displayed in UI
        /// </summary>
        public void RefreshScriptsVersion()
        {
            Dispatcher.Invoke(() =>
            {
                var scriptsVersion = GetLocalScriptVersion();
                ConnectionTab.ScriptsVersionTextCtrl.Text = scriptsVersion ?? "Not installed";
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

        /// <summary>
        /// Mostra notificação toast que desaparece após 10 segundos
        /// </summary>
        private void ShowToast(string message, string icon = "ℹ️", int durationSeconds = 10)
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
