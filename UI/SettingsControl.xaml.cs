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
            _deviceRefreshTimer.Tick += (s, e) => LoadAvailableDevices();
            _deviceRefreshTimer.Start();

            // 🔥 Timer SIMPLIFICADO - apenas detectar crashes APÓS conectar
            _connectionStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) // A cada 5 segundos
            };
            _connectionStatusTimer.Tick += (s, e) => CheckScriptStatusPeriodic();
            _connectionStatusTimer.Start();
        }

        private void InitializeData()
        {
            // Contacts
            _contacts = new ObservableCollection<Contact>(_settings.Contacts);
            ContactsDataGrid.ItemsSource = _contacts;

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
            KeywordsListBox.ItemsSource = _keywords;

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
                TargetDeviceComboBox.Items.Clear();

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
                    TargetDeviceComboBox.Items.Add(savedItem);
                    TargetDeviceComboBox.SelectedIndex = 0;
                }

                // Adicionar outros VoCores online (se não forem o salvo)
                foreach (var device in devices.Where(d => d.Id != _settings.TargetDevice))
                {
                    var item = new ComboBoxItem
                    {
                        Content = $"{device.Name} ✅",
                        Tag = device.Id
                    };
                    TargetDeviceComboBox.Items.Add(item);
                }

                // Se não houver devices E não houver device salvo
                if (TargetDeviceComboBox.Items.Count == 0)
                {
                    var placeholder = new ComboBoxItem
                    {
                        Content = "No VoCore detected - connect and refresh",
                        IsEnabled = false
                    };
                    TargetDeviceComboBox.Items.Add(placeholder);
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
                TargetDeviceComboBox.Items.Clear();
                TargetDeviceComboBox.Items.Add(errorItem);
            }
            finally
            {
                _isLoadingDevices = false; // 🔥 Desbloquear SelectionChanged
            }
        }

        private void UpdateDeviceStatus(int connectedCount)
        {
            // Atualizar label de status ao lado do Refresh
            Dispatcher.Invoke(() =>
            {
                if (DeviceStatusLabel != null)
                {
                    if (connectedCount > 0)
                    {
                        DeviceStatusLabel.Text = $"✅ {connectedCount} VoCore(s) connected";
                        DeviceStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                    }
                    else
                    {
                        DeviceStatusLabel.Text = "❌ No VoCores detected";
                        DeviceStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(231, 72, 119));
                    }
                }
            });
        }

        private void LoadSettings()
        {
            try
            {
                // 🔧 Carregar Backend Mode
                foreach (ComboBoxItem item in BackendModeCombo.Items)
                {
                    if (item.Tag?.ToString() == _settings.BackendMode)
                    {
                        BackendModeCombo.SelectedItem = item;
                        break;
                    }
                }

                // 🔧 Carregar Target Device (se salvo)
                if (!string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    foreach (ComboBoxItem item in TargetDeviceComboBox.Items)
                    {
                        if (item.Tag?.ToString() == _settings.TargetDevice)
                        {
                            TargetDeviceComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Sliders - converter de ms para segundos onde necessário
                MaxMessagesPerContactSlider.Value = _settings.MaxGroupSize;
                MaxQueueSizeSlider.Value = _settings.MaxQueueSize;
                NormalDurationSlider.Value = _settings.NormalDuration / 1000; // ms → seconds
                UrgentDurationSlider.Value = _settings.UrgentDuration / 1000; // ms → seconds

                // Checkbox RemoveAfterFirstDisplay
                RemoveAfterFirstDisplayCheckbox.IsChecked = _settings.RemoveAfterFirstDisplay;

                // ReminderInterval slider (ms → minutes)
                ReminderIntervalSlider.Value = _settings.ReminderInterval / 60000;

                // Mostrar/esconder painel baseado no checkbox
                ReminderIntervalPanel.Visibility = _settings.RemoveAfterFirstDisplay ? Visibility.Collapsed : Visibility.Visible;

                // Quick replies - apenas textos
                Reply1TextBox.Text = _settings.Reply1Text;
                Reply2TextBox.Text = _settings.Reply2Text;

                ShowConfirmationCheck.IsChecked = _settings.ShowConfirmation;

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

            var currentStatus = StatusText.Text.ToLower();

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
                StatusText.Text = status;

                switch (status.ToLower())
                {
                    case "connected":
                        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(14, 122, 13)); // Green

                        // Disconnect enabled, Reconnect disabled
                        DisconnectButton.IsEnabled = true;
                        DisconnectButton.Opacity = 1.0;
                        ReconnectButton.IsEnabled = false;
                        ReconnectButton.Opacity = 0.5;

                        ConnectedNumberText.Text = number != null ? $"Connected as: +{number}" : "Connected";

                        // ✅ ESCONDER QR CODE quando conecta
                        QRCodeImage.Visibility = Visibility.Collapsed;
                        QRCodeInstructions.Visibility = Visibility.Collapsed;
                        break;

                    case "connecting":
                        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Disconnect deve estar sempre disponível
                        DisconnectButton.IsEnabled = true;
                        DisconnectButton.Opacity = 1.0;
                        ReconnectButton.IsEnabled = false;
                        ReconnectButton.Opacity = 0.5;
                        ConnectedNumberText.Text = "Connecting to WhatsApp...";
                        break;

                    case "qr":
                        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Disconnect deve estar sempre disponível
                        DisconnectButton.IsEnabled = true;
                        DisconnectButton.Opacity = 1.0;
                        ReconnectButton.IsEnabled = false;
                        ReconnectButton.Opacity = 0.5;
                        ConnectedNumberText.Text = "Waiting for QR code scan...";
                        break;


                    case "node.js not installed":
                        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange

                        // Both disabled
                        DisconnectButton.IsEnabled = false;
                        DisconnectButton.Opacity = 0.5;
                        ReconnectButton.IsEnabled = false;
                        ReconnectButton.Opacity = 0.5;

                        ConnectedNumberText.Text = "Node.js is not installed. Please install Node.js from nodejs.org";
                        break;

                    case "disconnected":
                    case "error":
                        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(231, 72, 119)); // Red

                        // Disconnect disabled, Reconnect enabled
                        DisconnectButton.IsEnabled = false;
                        DisconnectButton.Opacity = 0.5;
                        ReconnectButton.IsEnabled = true;
                        ReconnectButton.Opacity = 1.0;

                        ConnectedNumberText.Text = "No connection";
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

                    QRCodeImage.Source = bitmap;
                    QRCodeImage.Visibility = Visibility.Visible;
                    QRCodeInstructions.Visibility = Visibility.Visible;
                }
                catch (Exception)
                {
                    // Se falhar, mostrar mensagem
                    QRCodeInstructions.Text = $"Scan this QR Code with WhatsApp:\n{qrData}";
                    QRCodeInstructions.Visibility = Visibility.Visible;
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
                QRCodeImage.Visibility = Visibility.Collapsed;
                QRCodeInstructions.Visibility = Visibility.Collapsed;
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

        private void TestOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Verificar se há device selecionado
                if (string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    ShowToast("Please select a VoCore device first!", "⚠️", 5);
                    return;
                }

                // ✅ NOVO TESTE: Não muda VoCore, não muda dashboard, só mostra mensagem
                _plugin.ShowTestMessage();

                ShowToast("Testing selected VoCore for 5s", "✅", 5);
            }
            catch (Exception ex)
            {
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
                if (TargetDeviceComboBox.SelectedItem is ComboBoxItem selectedItem)
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
            if (MaxMessagesPerContactValue != null && _settings != null)
            {
                int value = (int)e.NewValue;
                MaxMessagesPerContactValue.Text = value.ToString();
                _settings.MaxGroupSize = value;
            }
        }

        private void MaxQueueSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MaxQueueSizeValue != null && _settings != null)
            {
                int value = (int)e.NewValue;
                MaxQueueSizeValue.Text = value.ToString();
                _settings.MaxQueueSize = value;
            }
        }

        private void NormalDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (NormalDurationValue != null && _settings != null)
            {
                int value = (int)e.NewValue;
                NormalDurationValue.Text = $"{value}s";
                _settings.NormalDuration = value * 1000; // Convert to milliseconds
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void UrgentDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (UrgentDurationValue != null && _settings != null)
            {
                int value = (int)e.NewValue;
                UrgentDurationValue.Text = $"{value}s";
                _settings.UrgentDuration = value * 1000; // Convert to milliseconds
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void Reply1TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Reply1Text = Reply1TextBox.Text.Trim();
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void Reply2TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.Reply2Text = Reply2TextBox.Text.Trim();
                _plugin?.SaveSettings(); // 💾 SALVAR
            }
        }

        private void RemoveAfterFirstDisplayCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings == null)
                return;

            bool isChecked = RemoveAfterFirstDisplayCheckbox.IsChecked == true;
            _settings.RemoveAfterFirstDisplay = isChecked;

            // 💾 SALVAR SETTINGS AUTOMATICAMENTE
            _plugin.SaveSettings();

            // ✅ Se ativou RemoveAfterFirstDisplay, limpar mensagens VIP/URGENT antigas
            if (isChecked)
            {
                _plugin.ClearVipUrgentQueue();
            }

            // Mostrar/esconder painel do ReminderInterval
            if (ReminderIntervalPanel != null)
            {
                ReminderIntervalPanel.Visibility = isChecked ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void ReminderIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ReminderIntervalValue != null && _settings != null)
            {
                int value = (int)e.NewValue;
                ReminderIntervalValue.Text = $"{value} min";
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
                ChatContactsComboBox.ItemsSource = _chatContacts;

                if (contacts != null && contacts.Count > 0)
                {
                    ChatContactsComboBox.IsEnabled = true;
                    AddFromChatsButton.IsEnabled = true;
                    RefreshChatsButton.IsEnabled = true;
                    ChatsStatusText.Text = $"✅ {contacts.Count} contacts from active chats";
                    ChatsStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                else
                {
                    ChatContactsComboBox.IsEnabled = false;
                    AddFromChatsButton.IsEnabled = false;
                    RefreshChatsButton.IsEnabled = false;
                    ChatsStatusText.Text = "⚠️ No active chats found";
                    ChatsStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                }
            });
        }

        /// <summary>
        /// Adicionar contacto das conversas à lista de allowed
        /// </summary>
        private void AddFromChatsButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ChatContactsComboBox.SelectedItem as Contact;

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

            ChatContactsComboBox.SelectedIndex = -1;

            ShowToast($"{newContact.Name} adicionado aos contactos permitidos!", "✅");
        }

        /// <summary>
        /// Refresh lista de contactos das conversas
        /// </summary>
        private void RefreshChatsButton_Click(object sender, RoutedEventArgs e)
        {
            // Atualizar UI para mostrar que está a refreshar
            ChatsStatusText.Text = "🔄 Refreshing contacts...";
            ChatsStatusText.Foreground = System.Windows.Media.Brushes.Orange;

            RefreshChatsButton.IsEnabled = false;

            // Pedir ao plugin para refresh
            _plugin.RefreshChatContacts();

            // O botão será reativado quando UpdateChatContactsList() for chamado
        }

        #endregion

        #region Keywords Tab

        private void AddKeyword_Click(object sender, RoutedEventArgs e)
        {
            var keyword = NewKeyword.Text.Trim().ToLowerInvariant();

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

            NewKeyword.Clear();
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

            if (TargetDeviceComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
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
            string name = ManualNameTextBox.Text.Trim();
            string number = ManualNumberTextBox.Text.Trim();

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
            ManualNameTextBox.Text = "Name";
            ManualNumberTextBox.Text = "+351...";

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
            if (BackendModeCombo.SelectedItem == null) return;

            var selected = BackendModeCombo.SelectedItem as ComboBoxItem;
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
                ShowToast($"A mudar para {selected.Content}...", "🔄", 3);

                // Fazer switch do backend (para, aguarda, cria novo, inicia)
                await _plugin.SwitchBackend(newMode);

                ShowToast($"{selected.Content} conectado com sucesso!", "✅", 5);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erro ao mudar backend: {ex.Message}",
                    "Erro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
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

            ContactsDataGrid.Visibility = _contacts.Count > 0
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
                    if (Reply1ControlEditorPlaceholder != null)
                    {
                        Reply1ControlEditorPlaceholder.Content = reply1Editor;
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
                    if (Reply2ControlEditorPlaceholder != null)
                    {
                        Reply2ControlEditorPlaceholder.Content = reply2Editor;
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
            ChatContactsComboBox.IsEnabled = !isBaileys;
            RefreshChatsButton.IsEnabled = !isBaileys;
            AddFromChatsButton.IsEnabled = !isBaileys;

            // Atualizar texto de status
            if (isBaileys)
            {
                ChatsStatusText.Text = "⚠️ Chat contacts list is not supported with Baileys backend. Please use WhatsApp-Web.js.";
                ChatsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange
            }
            else
            {
                // Restaurar texto original (será atualizado quando carregar contactos)
                ChatsStatusText.Text = "Click Refresh to load contacts from active chats";
                ChatsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(133, 133, 133)); // Gray
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
                WhatsAppWebJsManualRadio.IsChecked = true;
                WhatsAppWebJsManualPanel.Visibility = Visibility.Visible;
                WhatsAppWebJsRepoTextBox.Text = _settings.WhatsAppWebJsManualRepo;
            }
            else
            {
                WhatsAppWebJsOfficialRadio.IsChecked = true;
                WhatsAppWebJsManualPanel.Visibility = Visibility.Collapsed;
            }

            // Baileys source
            if (_settings.BaileysSource == "manual")
            {
                BaileysManualRadio.IsChecked = true;
                BaileysManualPanel.Visibility = Visibility.Visible;
                BaileysRepoTextBox.Text = _settings.BaileysManualRepo;
            }
            else
            {
                BaileysOfficialRadio.IsChecked = true;
                BaileysManualPanel.Visibility = Visibility.Collapsed;
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
                        WhatsAppWebJsVersionCombo.Items.Clear();

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
                            WhatsAppWebJsVersionCombo.Items.Add(mainItem);
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
                                WhatsAppWebJsVersionCombo.Items.Add(installedItem);
                            }
                        }

                        // Baileys - ler do package.json
                        var baileysSpec = deps["@whiskeysockets/baileys"]?.ToString();
                        BaileysVersionCombo.Items.Clear();

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
                            BaileysVersionCombo.Items.Add(latestItem);
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
                                BaileysVersionCombo.Items.Add(installedItem);
                            }
                        }
                    }
                }

                // Scripts version
                var scriptsVersion = GetLocalScriptVersion();
                ScriptsVersionText.Text = scriptsVersion ?? "Not installed";
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
            WhatsAppWebJsCheckButton.IsEnabled = false;
            WhatsAppWebJsCheckButton.Content = "Checking...";

            try
            {
                var versions = await FetchNpmVersionsAsync("whatsapp-web.js");

                if (versions.Count > 0)
                {
                    _whatsappWebJsVersions = versions;
                    var currentVersion = _settings.WhatsAppWebJsVersion;

                    // Guardar item selecionado atual
                    var currentlySelected = WhatsAppWebJsVersionCombo.SelectedItem as ComboBoxItem;
                    var existingItems = WhatsAppWebJsVersionCombo.Items.Cast<ComboBoxItem>().ToList();

                    // Adicionar versões stable do npm (últimas 10) que ainda não existem
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
                            WhatsAppWebJsVersionCombo.Items.Add(item);
                        }
                    }

                    // Restaurar seleção
                    if (currentlySelected != null)
                    {
                        WhatsAppWebJsVersionCombo.SelectedItem = currentlySelected;
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
                        WhatsAppWebJsUpdateBadge.Visibility = Visibility.Visible;
                        ShowToast($"New whatsapp-web.js version available: {versions[0]}", "🆕", 5);
                    }
                    else
                    {
                        WhatsAppWebJsUpdateBadge.Visibility = Visibility.Collapsed;
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
                WhatsAppWebJsCheckButton.IsEnabled = true;
                WhatsAppWebJsCheckButton.Content = "Check for updates";
            }
        }

        /// <summary>
        /// Check for baileys updates from npm registry
        /// </summary>
        private async void BaileysCheckButton_Click(object sender, RoutedEventArgs e)
        {
            BaileysCheckButton.IsEnabled = false;
            BaileysCheckButton.Content = "Checking...";

            try
            {
                var versions = await FetchNpmVersionsAsync("@whiskeysockets/baileys");

                if (versions.Count > 0)
                {
                    _baileysVersions = versions;
                    var currentVersion = _settings.BaileysVersion;

                    // Guardar item selecionado atual
                    var currentlySelected = BaileysVersionCombo.SelectedItem as ComboBoxItem;
                    var existingItems = BaileysVersionCombo.Items.Cast<ComboBoxItem>().ToList();

                    // Adicionar versões stable do npm (últimas 10) que ainda não existem
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
                            BaileysVersionCombo.Items.Add(item);
                        }
                    }

                    // Restaurar seleção
                    if (currentlySelected != null)
                    {
                        BaileysVersionCombo.SelectedItem = currentlySelected;
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
                        BaileysUpdateBadge.Visibility = Visibility.Visible;
                        ShowToast($"New baileys version available: {versions[0]}", "🆕", 5);
                    }
                    else
                    {
                        BaileysUpdateBadge.Visibility = Visibility.Collapsed;
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
                BaileysCheckButton.IsEnabled = true;
                BaileysCheckButton.Content = "Check for updates";
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
                    // Get all versions and sort descending
                    versions = versionsObj.Properties()
                        .Select(p => p.Name)
                        .Where(v => !v.Contains("-")) // Exclude pre-release versions
                        .OrderByDescending(v => new Version(
                            Regex.Replace(v, @"[^\d.]", "").TrimEnd('.')))
                        .Take(10)
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
            if (WhatsAppWebJsVersionCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var selectedVersion = item.Tag.ToString();

                // Show/hide Install button based on selection
                if (selectedVersion != _settings.WhatsAppWebJsVersion)
                {
                    WhatsAppWebJsInstallButton.Visibility = Visibility.Visible;
                }
                else
                {
                    WhatsAppWebJsInstallButton.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Handle version selection change for baileys
        /// </summary>
        private void BaileysVersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BaileysVersionCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var selectedVersion = item.Tag.ToString();

                // Show/hide Install button based on selection
                if (selectedVersion != _settings.BaileysVersion)
                {
                    BaileysInstallButton.Visibility = Visibility.Visible;
                }
                else
                {
                    BaileysInstallButton.Visibility = Visibility.Collapsed;
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
                            WhatsAppWebJsUpdateBadge.Visibility = Visibility.Collapsed;
                        }
                        else if (packageName.Contains("baileys"))
                        {
                            _settings.BaileysVersion = version;
                            BaileysUpdateBadge.Visibility = Visibility.Collapsed;
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
            if (WhatsAppWebJsVersionCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
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
            if (BaileysVersionCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
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

                // Step 1: Disconnect WhatsApp
                _plugin.DisconnectWhatsApp();
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
                                WhatsAppWebJsInstallButton.Visibility = Visibility.Collapsed;
                            }
                            else if (packageName.Contains("baileys"))
                            {
                                _settings.BaileysVersion = version;
                                BaileysInstallButton.Visibility = Visibility.Collapsed;
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

            if (WhatsAppWebJsManualRadio.IsChecked == true)
            {
                WhatsAppWebJsManualPanel.Visibility = Visibility.Visible;
                _settings.WhatsAppWebJsSource = "manual";
            }
            else
            {
                WhatsAppWebJsManualPanel.Visibility = Visibility.Collapsed;
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

            if (BaileysManualRadio.IsChecked == true)
            {
                BaileysManualPanel.Visibility = Visibility.Visible;
                _settings.BaileysSource = "manual";
            }
            else
            {
                BaileysManualPanel.Visibility = Visibility.Collapsed;
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
            var repo = WhatsAppWebJsRepoTextBox.Text.Trim();

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
            var repo = BaileysRepoTextBox.Text.Trim();

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
                if (WhatsAppWebJsVersionCombo.Items.Count > 0)
                {
                    WhatsAppWebJsVersionCombo.SelectedIndex = 0;
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
                if (BaileysVersionCombo.Items.Count > 0)
                {
                    BaileysVersionCombo.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Check for scripts updates from GitHub
        /// </summary>
        private async void ScriptsCheckButton_Click(object sender, RoutedEventArgs e)
        {
            ScriptsCheckButton.IsEnabled = false;
            ScriptsCheckButton.Content = "Checking...";

            try
            {
                // Get local script version
                var localVersion = GetLocalScriptVersion();

                // Get GitHub version
                var githubVersion = await FetchGitHubScriptVersionAsync();

                if (githubVersion != null && localVersion != githubVersion)
                {
                    _latestScriptVersion = githubVersion;
                    ScriptsUpdateBadge.Visibility = Visibility.Visible;
                    ShowToast($"New scripts version available: {githubVersion} (current: {localVersion})", "🆕", 5);
                }
                else
                {
                    ScriptsUpdateBadge.Visibility = Visibility.Collapsed;
                    ShowToast($"Scripts are up to date (v{localVersion})", "✅", 3);
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error checking scripts: {ex.Message}", "❌", 5);
            }
            finally
            {
                ScriptsCheckButton.IsEnabled = true;
                ScriptsCheckButton.Content = "Check for updates";
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

                ScriptsUpdateBadge.Visibility = Visibility.Collapsed;
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
                    NodeJsStatusIcon.Text = "❌";
                    NodeJsStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    NodeJsStatusIcon.Text = "✓";
                    NodeJsStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                }
                else
                {
                    NodeJsStatusIcon.Text = "⏳";
                    NodeJsStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                NodeJsStatusText.Text = $"Node.js: {status}";
                UpdateDependenciesOverallStatus();
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
                    GitStatusIcon.Text = "✓";
                    GitStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                }
                else
                {
                    GitStatusIcon.Text = "⏳";
                    GitStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                GitStatusText.Text = $"Git: {status}";
                UpdateDependenciesOverallStatus();
            });
        }

        public void UpdateNpmStatus(string status, bool isComplete, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (isError)
                {
                    NpmPackagesStatusIcon.Text = "❌";
                    NpmPackagesStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (isComplete)
                {
                    NpmPackagesStatusIcon.Text = "✓";
                    NpmPackagesStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
                }
                else
                {
                    NpmPackagesStatusIcon.Text = "⏳";
                    NpmPackagesStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                NpmPackagesStatusText.Text = $"Npm packages: {status}";
                UpdateDependenciesOverallStatus();
            });
        }

        public void SetDependenciesInstalling(bool isInstalling, string progressMessage = "")
        {
            Dispatcher.Invoke(() =>
            {
                if (isInstalling)
                {
                    DependenciesStatusText.Text = "🔄 Installing dependencies...";
                    DependenciesStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                    DependenciesProgressText.Text = progressMessage;
                    DependenciesProgressText.Visibility = Visibility.Visible;

                    // Disable connection buttons during installation
                    DisconnectButton.IsEnabled = false;
                    ReconnectButton.IsEnabled = false;
                }
                else
                {
                    DependenciesProgressText.Visibility = Visibility.Collapsed;
                    UpdateDependenciesOverallStatus();

                    // Re-enable connection buttons after installation
                    // Check connection state to enable appropriate buttons
                    bool isConnected = StatusText.Text == "Connected";
                    DisconnectButton.IsEnabled = isConnected;
                    ReconnectButton.IsEnabled = true;
                }
            });
        }

        private void UpdateDependenciesOverallStatus()
        {
            Dispatcher.Invoke(() =>
            {
                bool nodeOk = NodeJsStatusIcon.Text == "✓";
                bool gitOk = GitStatusIcon.Text == "✓";
                bool npmOk = NpmPackagesStatusIcon.Text == "✓";

                bool anyError = NodeJsStatusIcon.Text == "❌" ||
                                GitStatusIcon.Text == "❌" ||
                                NpmPackagesStatusIcon.Text == "❌";

                bool anyPending = NodeJsStatusIcon.Text == "⏳" ||
                                  GitStatusIcon.Text == "⏳" ||
                                  NpmPackagesStatusIcon.Text == "⏳";

                if (anyError)
                {
                    DependenciesStatusText.Text = "❌ Installation failed";
                    DependenciesStatusText.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                }
                else if (anyPending)
                {
                    DependenciesStatusText.Text = "⏳ Installing...";
                    DependenciesStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                }
                else if (nodeOk && gitOk && npmOk)
                {
                    DependenciesStatusText.Text = "✅ All dependencies installed";
                    DependenciesStatusText.Foreground = new SolidColorBrush(Color.FromRgb(14, 122, 13));
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
