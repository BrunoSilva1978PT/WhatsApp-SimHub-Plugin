using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WhatsAppSimHubPlugin.Models;
using Expr = System.Linq.Expressions.Expression;

namespace WhatsAppSimHubPlugin.UI
{
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
            _contacts.CollectionChanged += (s, e) => {
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

                // Quick replies - apenas textos
                Reply1TextBox.Text = _settings.Reply1Text;
                Reply2TextBox.Text = _settings.Reply2Text;

                ShowConfirmationCheck.IsChecked = _settings.ShowConfirmation;

                // Atualizar UI da tab Contacts baseado no backend
                UpdateContactsTabForBackend();

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Verificar se o script Node.js está a correr
            CheckScriptStatus();
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
                MessageBox.Show($"Error disconnecting: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plugin.RefreshDevices();
                LoadAvailableDevices();
                MessageBox.Show("Devices refreshed! VoCores should now appear if connected.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing devices: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Verificar se há device selecionado
                if (string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    MessageBox.Show("Please select a VoCore device first!",
                        "No Device Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // ✅ NOVO TESTE: Não muda VoCore, não muda dashboard, só mostra mensagem
                _plugin.ShowTestMessage();

                MessageBox.Show($"Test message displayed for 5 seconds on {_settings.TargetDevice}!",
                    "Test Sent", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error testing overlay: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                _settings.EnableGrouping = (value > 1); // Auto-enable if > 1
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
                MessageBox.Show($"Error reconnecting: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("Please select a contact from the list.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verificar se já existe
            var existing = _contacts.FirstOrDefault(c =>
                c.Number.Replace("+", "").Replace(" ", "").Replace("-", "") ==
                selected.Number.Replace("+", "").Replace(" ", "").Replace("-", ""));

            if (existing != null)
            {
                MessageBox.Show($"{existing.Name} is already in your allowed contacts list.", "Already Added",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show("Please enter a keyword", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verificar duplicados (case-insensitive)
            if (_keywords.Any(k => k.ToLowerInvariant() == keyword))
            {
                MessageBox.Show("This keyword already exists", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show("Please enter a name.", "Name Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(number) || number == "+351..." || !number.StartsWith("+"))
            {
                MessageBox.Show("Please enter a valid phone number.\n\nFormat: +[country code][number]\nExample: +351912345678",
                    "Invalid Number", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Verificar duplicado
            var existing = _contacts.FirstOrDefault(c =>
                c.Number.Replace("+", "").Replace(" ", "").Replace("-", "") ==
                number.Replace("+", "").Replace(" ", "").Replace("-", ""));

            if (existing != null)
            {
                MessageBox.Show("A contact with this number already exists.", "Duplicate",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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

            MessageBox.Show($"✅ {contact.Name} added to allowed contacts!", "Added",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(
                    $"Backend mudado para {selected.Content}.\n\nA fazer reconnect automático...",
                    "Backend Mudado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                // Fazer switch do backend (para, aguarda, cria novo, inicia)
                await _plugin.SwitchBackend(newMode);

                MessageBox.Show(
                    $"Backend {selected.Content} conectado com sucesso!\n\nPodes fazer scan do QR code agora.",
                    "Reconnect Completo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
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

            EmptyContactsState.Visibility = _contacts.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

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
