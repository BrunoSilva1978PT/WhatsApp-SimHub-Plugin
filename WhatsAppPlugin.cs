using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using SimHub.Plugins;
using GameReaderCommon;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WhatsAppSimHubPlugin.Models;
using WhatsAppSimHubPlugin.Core;
using System.Timers;

namespace WhatsAppSimHubPlugin
{
    [PluginDescription("WhatsApp notifications during sim racing")]
    [PluginAuthor("Bruno Silva")]
    [PluginName("WhatsApp Plugin")]
    public class WhatsAppPlugin : IPlugin, IWPFSettingsV2, IDataPlugin
    {
        public PluginManager PluginManager { get; set; }
        public ImageSource PictureIcon => CreateWhatsAppIcon();
        public string LeftMenuTitle => "WhatsApp Plugin";

        private PluginSettings _settings;
        private WebSocketManager _nodeManager;
        private MessageQueue _messageQueue;
        private OverlayRenderer _overlayRenderer;
        private object _vocoreDevice; // Referência ao BitmapDisplayDevice do VoCore
        private object _vocoreSettings; // Settings do VoCore
        private DateTime _lastDashboardCheck = DateTime.MinValue; // 🔥 Throttle verificação dashboard
        private bool _isTestingMessage = false; // 🔥 Flag para bloquear queues durante teste
        private Timer _dashboardCheckTimer; // 🔥 Timer para verificar dashboard de 30 em 30s
        private DashboardInstaller _dashboardInstaller; // 🔥 Installer para reinstalar dashboard

        // 🎮 QUICK REPLIES: Agora funcionam via Actions registadas!
        // Ver RegisterActions() e SendQuickReply(int)
        private bool _replySentForCurrentMessage = false; // 🔒 Bloqueia múltiplos envios para mesma mensagem

        private string _pluginPath;
        private string _settingsFile;
        private string _contactsFile;
        private string _keywordsFile;
        private UI.SettingsControl _settingsControl;

        // 🆕 SETUP & DEPENDENCIES
        private DependencyManager _dependencyManager;
        private UI.SetupControl _setupControl;
        private bool _setupComplete = false;

        // Propriedade pública para acesso às configurações
        public PluginSettings Settings => _settings;

        // Propriedade para verificar se o script Node.js está a correr
        public bool IsScriptRunning => _nodeManager?.IsConnected ?? false;

        // Verificar se Node.js está instalado
        public bool IsNodeJsInstalled()
        {
            // Verificar se node.exe existe em locais comuns
            var nodePaths = new[]
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe"
            };

            foreach (var path in nodePaths)
            {
                if (System.IO.File.Exists(path))
                    return true;
            }

            // Tentar via PATH environment variable
            try
            {
                var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                proc.WaitForExit(1000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Criar ícone WhatsApp PRETO E BRANCO para menu SimHub
        /// </summary>
        private ImageSource CreateWhatsAppIcon()
        {
            try
            {
                var drawingGroup = new DrawingGroup();

                // Círculo externo (PRETO)
                var circlePen = new Pen(Brushes.Black, 2.5);
                drawingGroup.Children.Add(new GeometryDrawing(null, circlePen,
                    new EllipseGeometry(new Point(16, 16), 14, 14)));

                // Telefone + bolha (PRETO)
                var blackBrush = Brushes.Black;

                // Bolha do chat (canto inferior esquerdo)
                var bubblePath = "M 8,28 L 4,32 L 8,32 C 8,30.5 8,29 8,28 Z";
                drawingGroup.Children.Add(new GeometryDrawing(blackBrush, null,
                    Geometry.Parse(bubblePath)));

                // Telefone dentro do círculo
                var phonePath = "M 22,19 C 21.7,19.3 20.8,20.2 20.2,20.2 C 20,20.2 19.8,20.2 19.6,20.1 C 17.8,19.7 16.2,19 14.8,17.8 C 13.5,16.8 12.4,15.5 11.5,14 C 10.8,12.7 10.4,11.3 10.3,9.9 C 10.3,9.3 10.5,8.7 10.9,8.2 C 11.3,7.8 11.9,7.5 12.5,7.5 C 12.7,7.5 12.8,7.5 12.9,7.6 C 13.4,7.7 13.7,8.2 13.9,8.8 C 14.1,9.3 14.3,9.9 14.5,10.5 C 14.6,10.9 14.6,11.4 14.3,11.7 L 14.1,11.9 C 13.9,12.1 13.8,12.5 13.9,12.8 C 14.3,13.6 14.9,14.3 15.7,14.9 C 16.3,15.4 17.1,15.8 17.9,16 C 18.2,16.1 18.6,16 18.8,15.8 L 19,15.6 C 19.3,15.3 19.7,15.2 20.1,15.4 C 20.6,15.6 21.2,15.8 21.7,16 C 22.3,16.2 22.7,16.5 22.9,16.9 C 23,17.3 22.9,17.8 22.7,18.1 Z";
                drawingGroup.Children.Add(new GeometryDrawing(blackBrush, null,
                    Geometry.Parse(phonePath)));

                var drawingImage = new DrawingImage(drawingGroup);
                drawingImage.Freeze();
                return drawingImage;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Obter lista de VoCores disponíveis (APENAS VoCores, não monitores)
        /// </summary>
        public System.Collections.Generic.List<DeviceInfo> GetAvailableDevices()
        {
            var devices = new System.Collections.Generic.List<DeviceInfo>();

            try
            {
                // Usar reflection para aceder GetAllDevices
                var getAllDevicesMethod = PluginManager.GetType().GetMethod("GetAllDevices");
                if (getAllDevicesMethod == null) return devices;

                var devicesEnumerable = getAllDevicesMethod.Invoke(PluginManager, new object[] { true }) as System.Collections.IEnumerable;
                if (devicesEnumerable == null) return devices;

                // Iterar devices
                foreach (var device in devicesEnumerable)
                {
                    var deviceType = device.GetType();

                    // 🔥 FILTRAR: Só VoCores têm Settings.UseOverlayDashboard
                    // Monitores NÃO têm Information Overlay!
                    var settingsProp = deviceType.GetProperty("Settings");
                    if (settingsProp == null) continue;

                    var settings = settingsProp.GetValue(device);
                    if (settings == null) continue;

                    var settingsType = settings.GetType();
                    var overlayProp = settingsType.GetProperty("UseOverlayDashboard");

                    // Se NÃO tem UseOverlayDashboard → É monitor, ignorar!
                    if (overlayProp == null) continue;

                    // ✅ É VoCore! Adicionar à lista
                    var mainNameProp = deviceType.GetProperty("MainDisplayName");
                    var instanceIdProp = deviceType.GetProperty("InstanceId");
                    var serialProp = deviceType.GetProperty("SerialNumber");

                    var mainName = mainNameProp?.GetValue(device)?.ToString();
                    var instanceId = instanceIdProp?.GetValue(device)?.ToString();
                    var serial = serialProp?.GetValue(device)?.ToString();

                    if (!string.IsNullOrEmpty(mainName))
                    {
                        devices.Add(new DeviceInfo
                        {
                            Name = mainName,
                            Id = instanceId ?? mainName,
                            SerialNumber = serial ?? "N/A"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"GetAvailableDevices error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Refresh da lista de devices (não faz nada, devices são sempre atuais)
        /// </summary>
        public void RefreshDevices()
        {
            // GetAllDevices do SimHub sempre retorna lista atualizada
            // Não é necessário fazer refresh explícito
        }

        /// <summary>
        /// Re-attach ao VoCore e ativa overlay (chamado quando user muda device na UI)
        /// </summary>
        public void ReattachAndActivateOverlay()
        {
            // Re-attach ao VoCore
            AttachToVoCore();

            // Ativar overlay se attach foi bem sucedido
            if (_vocoreDevice != null && _vocoreSettings != null)
            {
                EnsureOverlayActive();
            }
            else
            {
                WriteLog("❌ Could not reattach to VoCore - overlay not activated");
            }
        }

        // Classe para informação de device
        public class DeviceInfo
        {
            public string Name { get; set; }
            public string Id { get; set; }
            public string SerialNumber { get; set; }
        }

        // ===== PROPRIEDADES PARA CONNECTION TAB =====
        private string _connectionStatus = "Disconnected";
        private string _connectedNumber = "";

        // ===== ESTADO INTERNO (NÃO EXPOR AO SIMHUB) =====
        private int _queueCount = 0;
        private List<QueuedMessage> _currentMessageGroup = null;
        private string _currentContactNumber = "";
        private string _currentContactRealNumber = "";  // Número real (ex: 351910203114) para enviar mensagens

        // ===== PROPRIEDADES PARA OVERLAY/DASHBOARD (EXPOSTAS AO SIMHUB) =====
        private bool _showMessage = false; // Controla visibilidade do overlay
        private string _overlaySender = "";
        private string _overlayTypeMessage = "";
        private int _overlayTotalMessages = 0;
        private string[] _overlayMessages = new string[10]; // Array de 10 mensagens

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            _pluginPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimHub", "WhatsAppPlugin");

            Directory.CreateDirectory(_pluginPath);

            // 🗑️ LIMPAR LOGS AO ARRANQUE (economia de espaço)
            try
            {
                var logsPath = Path.Combine(_pluginPath, "logs");
                if (Directory.Exists(logsPath))
                {
                    Directory.Delete(logsPath, true);
                }
            }
            catch { }

            // Inicializar array de mensagens vazias
            for (int i = 0; i < 10; i++)
            {
                _overlayMessages[i] = "";
            }

            _settingsFile = Path.Combine(_pluginPath, "config", "settings.json");
            _contactsFile = Path.Combine(_pluginPath, "config", "contacts.json");
            _keywordsFile = Path.Combine(_pluginPath, "config", "keywords.json");

            // Carregar configurações
            LoadSettings();

            // 🔥 VERIFICAR SE SETUP JÁ FOI COMPLETO (arquivo .setup-complete existe?)
            string setupFlagPath = Path.Combine(_pluginPath, ".setup-complete");
            if (File.Exists(setupFlagPath))
            {
                _setupComplete = true;
                WriteLog("✅ Setup already completed previously (found .setup-complete flag)");
            }
            else
            {
                WriteLog("⚠️ First run or setup not complete (no .setup-complete flag)");
            }

            // Inicializar componentes básicos
            _messageQueue = new MessageQueue(_settings, WriteLog);
            _messageQueue.OnGroupDisplay += MessageQueue_OnGroupDisplay;
            _messageQueue.OnMessageRemoved += MessageQueue_OnMessageRemoved;

            _nodeManager = new WebSocketManager(_pluginPath, _settings.BackendMode);
            _nodeManager.OnQrCode += NodeManager_OnQrCode;
            _nodeManager.OnReady += NodeManager_OnReady;
            _nodeManager.OnMessage += NodeManager_OnMessage;
            _nodeManager.OnError += NodeManager_OnError;
            _nodeManager.StatusChanged += NodeManager_OnStatusChanged;
            _nodeManager.ChatContactsListReceived += NodeManager_OnChatContactsListReceived;
            _nodeManager.ChatContactsError += NodeManager_OnChatContactsError;

            // Inicializar overlay renderer
            _overlayRenderer = new OverlayRenderer(_settings);

            // 📦 INSTALAR DASHBOARD AUTOMATICAMENTE
            WriteLog("=== Dashboard Installation ===");
            _dashboardInstaller = new DashboardInstaller(PluginManager, WriteLog);
            bool installed = _dashboardInstaller.InstallDashboard();

            if (installed)
            {
                WriteLog("✅ Dashboard installation completed successfully");
            }
            else
            {
                WriteLog("⚠️ Dashboard installation failed or dashboard already exists");
            }

            // Verificar se dashboard está acessível
            bool dashExists = _dashboardInstaller.IsDashboardInstalled();
            WriteLog($"Dashboard accessible: {dashExists}");

            // 🔥 INICIAR TIMER: Verificar dashboard de 30 em 30s
            _dashboardCheckTimer = new Timer(30000);
            _dashboardCheckTimer.Elapsed += DashboardCheckTimer_Elapsed;
            _dashboardCheckTimer.AutoReset = true;
            _dashboardCheckTimer.Start();
            WriteLog("✅ Dashboard auto-check timer started (30s interval)");

            // 🎮 IDataPlugin vai chamar DataUpdate() automaticamente a 60 FPS!
            // Não precisa de timer manual para botões!
            WriteLog("✅ IDataPlugin enabled - button detection ready (60 FPS)");

            // Registar propriedades no SimHub
            RegisterProperties();

            // Registar ações
            RegisterActions();

            // 🆕 INICIAR PROCESSO DE SETUP (verificar e instalar dependências)
            WriteLog("=== Starting Dependency Setup ===");
            _ = InitializeDependenciesAsync();

            // Log de inicialização
            WriteLog("=== WhatsApp Plugin Initialized ===");
            WriteLog($"Plugin path: {_pluginPath}");
            WriteLog($"Contacts: {_settings.Contacts.Count}");
            WriteLog($"Keywords: {string.Join(", ", _settings.Keywords)}");
        }

        public void WriteLog(string message)
        {
            try
            {
                // UM SÓ FICHEIRO: plugin.log (minimalista)
                var logPath = Path.Combine(_pluginPath, "logs", "plugin.log");
                var logDir = Path.GetDirectoryName(logPath);

                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // Formato minimalista: [HH:mm:ss] mensagem
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch
            {
                // Ignorar erros de log
            }
        }

        private void RegisterProperties()
        {
            // ===== CONNECTION PROPERTIES =====
            this.AttachDelegate("ConnectionStatus", () => _connectionStatus);
            this.AttachDelegate("ConnectedNumber", () => _connectedNumber);

            // ===== OVERLAY PROPERTIES (PARA DASHBOARD) =====
            // SimHub adiciona prefixo "WhatsAppPlugin." automaticamente!
            this.AttachDelegate("showmessage", () => _showMessage); // WhatsAppPlugin.showmessage
            this.AttachDelegate("sender", () => _overlaySender); // WhatsAppPlugin.sender
            this.AttachDelegate("typemessage", () => _overlayTypeMessage); // WhatsAppPlugin.typemessage
            this.AttachDelegate("totalmessages", () => _overlayTotalMessages); // WhatsAppPlugin.totalmessages

            // Array de 10 mensagens: WhatsAppPlugin.message[0] a WhatsAppPlugin.message[9]
            for (int i = 0; i < 10; i++)
            {
                int index = i; // Capturar valor para closure
                this.AttachDelegate($"message[{index}]", () => _overlayMessages[index]);
            }
        }

        private void RegisterActions()
        {
            WriteLog("[ACTIONS] 🔧 Starting RegisterActions()...");

            // 🎮 Actions - aparecem em Controls & Events
            // IMPORTANTE: SimHub adiciona automaticamente "WhatsAppPlugin." como prefixo!
            // Então registamos "SendReply1" e SimHub transforma em "WhatsAppPlugin.SendReply1"
            WriteLog("[ACTIONS] Registering SendReply1...");
            this.AddAction("SendReply1", (a, b) =>
            {
                try
                {
                    WriteLog($"[ACTION] SendReply1 triggered");
                    SendQuickReply(1);
                }
                catch (Exception ex)
                {
                    WriteLog($"[ACTION ERROR] SendReply1: {ex.Message}");
                }
            });
            WriteLog("[ACTIONS] ✅ SendReply1 registered");

            WriteLog("[ACTIONS] Registering SendReply2...");
            this.AddAction("SendReply2", (a, b) =>
            {
                try
                {
                    WriteLog($"[ACTION] SendReply2 triggered");
                    SendQuickReply(2);
                }
                catch (Exception ex)
                {
                    WriteLog($"[ACTION ERROR] SendReply2: {ex.Message}");
                }
            });
            WriteLog("[ACTIONS] ✅ SendReply2 registered");

            WriteLog("[ACTIONS] Registering DismissMessage...");
            this.AddAction("DismissMessage", (a, b) =>
            {
                try
                {
                    WriteLog($"[ACTION] DismissMessage lambda triggered!");
                    DismissCurrentMessage();
                }
                catch (Exception ex)
                {
                    WriteLog($"[ACTION ERROR] ❌ Exception in DismissMessage: {ex.Message}");
                }
            });
            WriteLog("[ACTIONS] ✅ DismissMessage registered (will appear as WhatsAppPlugin.DismissMessage in SimHub)");

            WriteLog("[ACTIONS] ✅✅✅ ALL ACTIONS REGISTERED SUCCESSFULLY ✅✅✅");
            WriteLog($"[ACTIONS] Total actions registered: 3");
            WriteLog($"[ACTIONS] They will appear in SimHub as:");
            WriteLog($"[ACTIONS]   - WhatsAppPlugin.SendReply1");
            WriteLog($"[ACTIONS]   - WhatsAppPlugin.SendReply2");
            WriteLog($"[ACTIONS]   - WhatsAppPlugin.DismissMessage");
        }

        private async void SendQuickReply(int replyNumber)
        {
            try
            {
                WriteLog($"[QUICK REPLY {replyNumber}] ⚡ Button pressed");

                // ✅ Usar mensagem que está MOSTRANDO no ecrã agora!
                if (_currentMessageGroup == null || _currentMessageGroup.Count == 0)
                {
                    WriteLog($"[QUICK REPLY] ❌ No message displayed");
                    return;
                }

                // 🔒 ONE-SHOT: Verificar se já enviou reply para esta mensagem
                if (_replySentForCurrentMessage)
                {
                    WriteLog($"[QUICK REPLY] ⚠️ Reply already sent - blocking duplicate");
                    return;
                }

                if (string.IsNullOrEmpty(_currentContactNumber))
                {
                    WriteLog($"[QUICK REPLY] ❌ No contact number");
                    return;
                }

                string replyText = replyNumber == 1 ? _settings.Reply1Text : _settings.Reply2Text;
                string contactName = _currentMessageGroup[0].From;
                string chatIdToSend = _currentMessageGroup[0].ChatId;

                WriteLog($"[QUICK REPLY {replyNumber}] 📤 Sending to {contactName}: {replyText}");

                // Send reply via WebSocket
                await _nodeManager.SendReplyAsync(chatIdToSend, replyText);

                // 🔒 MARCAR COMO ENVIADO
                _replySentForCurrentMessage = true;
                WriteLog($"[QUICK REPLY {replyNumber}] ✅ Reply sent successfully!");

                // Remover mensagens se configurado (já automático, sempre remove)
                _messageQueue.RemoveMessagesFromContact(_currentContactNumber);
                WriteLog($"[QUICK REPLY {replyNumber}] 🗑️ Removed messages from {contactName}");

                // Mostrar confirmação se configurado
                if (_settings.ShowConfirmation)
                {
                    ShowQuickReplyConfirmation(contactName);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[QUICK REPLY ERROR] ❌ {ex.Message}");
                WriteLog($"[QUICK REPLY ERROR] {ex.StackTrace}");
            }
        }

        private void DismissCurrentMessage()
        {
            if (!string.IsNullOrEmpty(_currentContactNumber))
            {
                _messageQueue.RemoveMessagesFromContact(_currentContactNumber);
            }
        }

        /// <summary>
        /// Atualiza propriedades do overlay para mostrar GRUPO de mensagens
        /// </summary>
        private void UpdateOverlayProperties(List<QueuedMessage> messages)
        {
            WriteLog($"[OVERLAY] ▶ UpdateOverlayProperties called - messages = {messages?.Count ?? 0}");

            if (messages == null || messages.Count == 0)
            {
                // Limpar overlay
                _showMessage = false;
                _overlaySender = "";
                _overlayTypeMessage = "";
                _overlayTotalMessages = 0;
                for (int i = 0; i < 10; i++)
                {
                    _overlayMessages[i] = "";
                }

                // 🔓 RESET: Permite novo envio quando mensagem desaparece
                _replySentForCurrentMessage = false;

                return;
            }

            var first = messages[0];

            // ✅ MOSTRAR OVERLAY
            _showMessage = true;

            // 🔓 RESET: Nova mensagem = permite novo envio
            _replySentForCurrentMessage = false;

            // Header - Sender (só o nome, sem contador)
            _overlaySender = first.From;

            // Header - Type (URGENT > VIP > "")
            if (messages.Any(m => m.IsUrgent))
                _overlayTypeMessage = "URGENT";
            else if (messages.Any(m => m.IsVip))
                _overlayTypeMessage = "VIP";
            else
                _overlayTypeMessage = "";

            // Header - Total messages
            _overlayTotalMessages = messages.Count;

            // Mensagens (array de 10, ordenadas por timestamp)
            var sortedMessages = messages.OrderBy(m => m.Timestamp).Take(10).ToList();
            for (int i = 0; i < 10; i++)
            {
                if (i < sortedMessages.Count)
                {
                    var msg = sortedMessages[i];
                    _overlayMessages[i] = FormatMessageForOverlay(msg);
                }
                else
                {
                    _overlayMessages[i] = "";
                }
            }

            WriteLog($"[OVERLAY] Showing {_overlaySender} ({_overlayTotalMessages} messages)");
        }

        /// <summary>
        /// Atualiza propriedades do overlay para mostrar mensagens no dashboard
        /// LEGACY: Usar versão com List<QueuedMessage> quando possível
        /// </summary>
        private void UpdateOverlayProperties(QueuedMessage message)
        {
            // 🔒 IGNORAR durante teste - NÃO ALTERAR NADA!
            if (_isTestingMessage) return;

            if (message == null)
            {
                // Limpar overlay quando não há mensagens
                _overlaySender = "";
                _overlayTypeMessage = "";
                _overlayTotalMessages = 0;
                for (int i = 0; i < 10; i++)
                {
                    _overlayMessages[i] = "";
                }
                return;
            }

            // Obter grupo de mensagens desta pessoa (mesmo número)
            var groupedMessages = _messageQueue
                .GetAllMessages()
                .Where(m => m.Number == message.Number)
                .OrderBy(m => m.Timestamp)
                .Take(10)
                .ToList();

            // Header - Sender
            _overlaySender = message.From;

            // Header - Type (URGENT > VIP > "")
            if (message.IsUrgent)
                _overlayTypeMessage = "URGENT";
            else if (message.IsVip)
                _overlayTypeMessage = "VIP";
            else
                _overlayTypeMessage = "";

            // Header - Total messages
            _overlayTotalMessages = groupedMessages.Count;

            // Mensagens (array de 10)
            for (int i = 0; i < 10; i++)
            {
                if (i < groupedMessages.Count)
                {
                    var msg = groupedMessages[i];
                    _overlayMessages[i] = FormatMessageForOverlay(msg);
                }
                else
                {
                    _overlayMessages[i] = ""; // Limpar mensagens vazias
                }
            }

            WriteLog($"[OVERLAY] Updated {_overlaySender} ({_overlayTotalMessages} messages)");
        }

        /// <summary>
        /// Formata mensagem para overlay: "HH:mm [mensagem até 36 chars ou 33 + ...]"
        /// </summary>
        private string FormatMessageForOverlay(QueuedMessage msg)
        {
            string timeStr = msg.Timestamp.ToString("HH:mm"); // 5 chars
            string body = msg.Body;

            // Limite CORRETO: hora (5) + espaço (1) + mensagem (47) = 53 chars
            // Se truncar: hora (5) + espaço (1) + texto (44) + "..." (3) = 53 chars
            const int maxMessageLength = 47;
            const int truncatedLength = 44;

            if (body.Length > maxMessageLength)
            {
                body = body.Substring(0, truncatedLength) + "...";
            }

            return $"{timeStr} {body}";
        }

        private void NodeManager_OnQrCode(object sender, string qrCode)
        {
            _settingsControl?.UpdateQRCode(qrCode);
            _settingsControl?.UpdateConnectionStatus("QR");
        }

        private void NodeManager_OnReady(object sender, (string number, string name) e)
        {
            _connectionStatus = "Connected";
            _connectedNumber = e.number;
            _settingsControl?.UpdateConnectionStatus("Connected", e.number);

            // 🔥 ESCONDER AVISO DE DISCONNECT
            _overlayRenderer?.Clear();

            WriteLog($"Connected to WhatsApp as {e.number}");
        }

        private void NodeManager_OnMessage(object sender, JObject messageData)
        {
            try
            {
                WriteLog($"Message received from WhatsApp: {messageData}");

                // ⭐ Node.js envia os dados DIRETOS (não em "message")
                var body = messageData["body"]?.ToString();
                var from = messageData["from"]?.ToString();
                var number = messageData["number"]?.ToString();
                var chatId = messageData["chatId"]?.ToString();

                WriteLog($"From: {from}, Number: {number}, Body: {body}");

                if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(number))
                {
                    WriteLog("IGNORED: Empty body or number");
                    return;
                }

                // Normalizar número (remover +, espaços, hífens)
                var normalizedNumber = number.Replace("+", "").Replace(" ", "").Replace("-", "");
                WriteLog($"📞 Received number: '{number}' → Normalized: '{normalizedNumber}'");

                // ⭐ VERIFICAR SE É DE CONTACTO PERMITIDO!
                WriteLog($"🔍 Checking against {_settings.Contacts.Count} contacts in allowed list:");

                foreach (var c in _settings.Contacts)
                {
                    var contactNumber = c.Number.Replace("+", "").Replace(" ", "").Replace("-", "");
                    WriteLog($"   Comparing '{normalizedNumber}' == '{contactNumber}' (Contact: {c.Name})");
                }

                var allowedContact = _settings.Contacts.FirstOrDefault(c =>
                {
                    var contactNumber = c.Number.Replace("+", "").Replace(" ", "").Replace("-", "");
                    return contactNumber == normalizedNumber;
                });

                if (allowedContact == null)
                {
                    WriteLog($"❌ REJECTED: Contact '{from}' (number: {number}) is NOT in allowed list!");
                    WriteLog($"   Add this number to your contacts: {number}");
                    return;  // ⭐ REJEITAR!
                }

                // ✅ Contacto permitido!
                WriteLog($"✅ ACCEPTED: Contact found in list: {allowedContact.Name} (VIP: {allowedContact.IsVip})");

                // ⭐ USAR NOME DO CONTACTO (não o "from" do WhatsApp que pode ser LinkedID)
                string displayName = allowedContact.Name;
                bool isVip = allowedContact.IsVip;

                // Verificar se contém keywords urgentes
                bool isUrgent = _settings.Keywords.Any(keyword =>
                    body.ToLowerInvariant().Contains(keyword.ToLowerInvariant()));

                if (isUrgent)
                {
                    WriteLog($"Message marked as URGENT (keyword detected)");
                }

                // ⭐ CRIAR MENSAGEM COM NOME DO CONTACTO
                var queuedMessage = new QueuedMessage
                {
                    From = displayName,  // ⭐ Nome do contacto da lista!
                    Number = number,
                    Body = body,
                    ChatId = chatId,
                    IsVip = isVip,
                    IsUrgent = isUrgent
                };

                WriteLog($"✅ QUEUED: From='{displayName}', VIP={isVip}, Urgent={isUrgent}");

                // Adicionar à fila
                _messageQueue.AddMessage(queuedMessage);

            }
            catch (Exception ex)
            {
                WriteLog($"ERROR processing message: {ex.Message}");
            }
        }

        private void NodeManager_OnError(object sender, EventArgs e)
        {
            WriteLog($"Node.js reported error or disconnected");

            _connectionStatus = "Error";
            _settingsControl?.UpdateConnectionStatus("Error");

            // 🔥 MOSTRAR AVISO NO OVERLAY
            _overlayRenderer?.SetSystemMessage("⚠️ WhatsApp Disconnected\nCheck SimHub settings");
        }

        private void NodeManager_OnStatusChanged(object sender, string status)
        {
            WriteLog($"📡 Status changed: {status}");

            if (status == "Installing")
            {
                _connectionStatus = "Installing dependencies...";
                _settingsControl?.UpdateConnectionStatus("Installing dependencies...");
            }
            else if (status == "Installed")
            {
                _connectionStatus = "Dependencies installed";
                _settingsControl?.UpdateConnectionStatus("Disconnected");
            }
            else if (status == "Starting")
            {
                _connectionStatus = "Starting Node.js...";
                _settingsControl?.UpdateConnectionStatus("Connecting");
            }
            else if (status == "Connected")
            {
                // Não fazer nada, o evento Ready vai tratar
            }
            else if (status.StartsWith("Error:"))
            {
                _connectionStatus = "Error";
                _settingsControl?.UpdateConnectionStatus("Error");
                WriteLog($"❌ ERROR: {status}");
            }
            else if (status.StartsWith("NodeError:"))
            {
                WriteLog($"🔴 NODE.JS ERROR: {status.Substring(10)}");
            }
            else if (status.StartsWith("NodeOutput:"))
            {
                WriteLog($"🟢 NODE.JS OUTPUT: {status.Substring(11)}");
            }
            else if (status.StartsWith("Debug:"))
            {
                // Logar mas não fazer nada no UI
                WriteLog($"🔍 {status}");
            }
        }

        private void NodeManager_OnChatContactsListReceived(object sender, JArray contactsArray)
        {
            try
            {
                WriteLog($"📱 Received {contactsArray.Count} contacts from active chats");

                var contacts = new System.Collections.ObjectModel.ObservableCollection<Contact>();

                foreach (var item in contactsArray)
                {
                    var name = item["name"]?.ToString() ?? "(No name)";
                    var number = item["number"]?.ToString();

                    if (!string.IsNullOrEmpty(number))
                    {
                        contacts.Add(new Contact
                        {
                            Name = name,
                            Number = number  // Já vem sem + (ex: 351910203114)
                        });
                    }
                }

                WriteLog($"✅ Parsed {contacts.Count} valid contacts");

                // Atualizar UI
                _settingsControl?.UpdateChatContactsList(contacts);
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error processing chat contacts: {ex.Message}");
            }
        }

        private void NodeManager_OnChatContactsError(object sender, string error)
        {
            WriteLog($"❌ Failed to load chat contacts: {error}");

            // Atualizar UI com erro
            _settingsControl?.UpdateChatContactsList(
                new System.Collections.ObjectModel.ObservableCollection<Contact>()
            );
        }

        private void MessageQueue_OnGroupDisplay(System.Collections.Generic.List<QueuedMessage> messages)
        {
            WriteLog($"[EVENT] ▶ OnGroupDisplay triggered - _isTestingMessage = {_isTestingMessage}, messages = {messages?.Count ?? 0}");

            // 🔒 IGNORAR mensagens durante teste
            if (_isTestingMessage)
            {
                WriteLog($"[EVENT] ⏸ OnGroupDisplay BLOCKED by _isTestingMessage");
                return;
            }

            if (messages != null && messages.Count > 0)
            {
                // ✅ GUARDAR GRUPO ATUAL (para Quick Reply)
                _currentMessageGroup = messages;
                _currentContactNumber = messages[0].ChatId;  // LinkedID ou chatId com @c.us
                _currentContactRealNumber = messages[0].Number;  // ⭐ Número real para enviar!

                WriteLog($"[EVENT] OnGroupDisplay: Saved chatId = {messages[0].ChatId}, realNumber = {messages[0].Number}");

                WriteLog($"[EVENT] Calling UpdateOverlayProperties with {messages.Count} messages...");

                // ✅ ATUALIZAR OVERLAY
                UpdateOverlayProperties(messages);

                // Atualizar contador interno
                _queueCount = _messageQueue.GetQueueSize();

                WriteLog($"[EVENT] ✅ OnGroupDisplay completed - displaying {messages.Count} messages from {messages[0].From}");
            }
        }

        private void MessageQueue_OnMessageRemoved()
        {
            WriteLog($"[EVENT] ▶ OnMessageRemoved triggered - _isTestingMessage = {_isTestingMessage}");

            // 🔒 IGNORAR durante teste
            if (_isTestingMessage)
            {
                WriteLog($"[EVENT] ⏸ OnMessageRemoved BLOCKED by _isTestingMessage");
                return;
            }

            // ✅ LIMPAR GRUPO ATUAL
            _currentMessageGroup = null;
            _currentContactNumber = "";
            _currentContactRealNumber = "";

            WriteLog($"[EVENT] Calling UpdateOverlayProperties(null) to clear overlay...");

            // ✅ LIMPAR OVERLAY
            UpdateOverlayProperties((List<QueuedMessage>)null);

            // Atualizar contador
            _queueCount = _messageQueue.GetQueueSize();

            WriteLog($"[EVENT] ✅ OnMessageRemoved completed - overlay cleared, queue count = {_queueCount}");
        }

        public void End(PluginManager pluginManager)
        {
            WriteLog("=== WhatsApp Plugin Shutting Down ===");

            // Parar timer de verificação do dashboard
            if (_dashboardCheckTimer != null)
            {
                _dashboardCheckTimer.Stop();
                _dashboardCheckTimer.Dispose();
                WriteLog("Dashboard check timer stopped");
            }

            SaveSettings();

            // 🔥 PARAR NODE.JS
            if (_nodeManager != null)
            {
                WriteLog("Stopping Node.js process...");
                _nodeManager.Stop();
                _nodeManager.Dispose();
                WriteLog("Node.js process stopped");
            }

            _messageQueue?.Dispose();

            // 🔥 MATAR PROCESSOS CHROME (puppeteer do whatsapp-web.js)
            try
            {
                WriteLog("Killing Chrome processes from WhatsApp plugin...");
                var chromeProcesses = System.Diagnostics.Process.GetProcessesByName("chrome");
                int killedCount = 0;

                foreach (var proc in chromeProcesses)
                {
                    try
                    {
                        // Tentar verificar se é Chrome do nosso plugin
                        // (vai estar na pasta do plugin ou com --user-data-dir do puppeteer)
                        var cmdLine = GetProcessCommandLine(proc);
                        if (cmdLine != null &&
                            (cmdLine.IndexOf("WhatsAppPlugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             cmdLine.IndexOf("puppeteer", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            WriteLog($"  Killing Chrome process {proc.Id}");
                            proc.Kill();
                            proc.WaitForExit(1000);
                            killedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"  Could not kill Chrome process {proc.Id}: {ex.Message}");
                    }
                }

                if (killedCount > 0)
                    WriteLog($"✅ Killed {killedCount} Chrome process(es)");
            }
            catch (Exception ex)
            {
                WriteLog($"⚠️ Error killing Chrome processes: {ex.Message}");
            }

            // 🔥 MATAR PROCESSOS NODE.JS RESTANTES
            try
            {
                WriteLog("Killing Node.js processes from WhatsApp plugin...");
                var nodeProcesses = System.Diagnostics.Process.GetProcessesByName("node");
                int killedCount = 0;

                foreach (var proc in nodeProcesses)
                {
                    try
                    {
                        var cmdLine = GetProcessCommandLine(proc);
                        if (cmdLine != null && cmdLine.IndexOf("whatsapp-client.js", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            WriteLog($"  Killing Node.js process {proc.Id}");
                            proc.Kill();
                            proc.WaitForExit(1000);
                            killedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"  Could not kill Node.js process {proc.Id}: {ex.Message}");
                    }
                }

                if (killedCount > 0)
                    WriteLog($"✅ Killed {killedCount} Node.js process(es)");
            }
            catch (Exception ex)
            {
                WriteLog($"⚠️ Error killing Node.js processes: {ex.Message}");
            }

            WriteLog("Plugin shutdown complete");
        }

        /// <summary>
        /// Helper para pegar command line de um processo
        /// </summary>
        private string GetProcessCommandLine(System.Diagnostics.Process process)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        return obj["CommandLine"]?.ToString();
                    }
                }
            }
            catch
            {
                // Se falhar, retornar null
            }
            return null;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    // ✅ Ficheiro existe - carregar SEM modificar
                    var json = File.ReadAllText(_settingsFile);
                    _settings = JsonConvert.DeserializeObject<PluginSettings>(json);

                    // NÃO chamar EnsureDefaults() aqui!
                    // Settings já existem, não modificar!
                }
                else
                {
                    // ✅ Primeira vez - criar settings novas COM defaults
                    _settings = new PluginSettings();
                    _settings.EnsureDefaults();
                    SaveSettings(); // Guardar logo para criar o ficheiro
                }
            }
            catch (Exception)
            {
                // ⚠️ Erro ao ler - criar novas
                _settings = new PluginSettings();
                _settings.EnsureDefaults();
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile));
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsFile, json);

                WriteLog($"✅ Settings saved: {_settings.Contacts.Count} contacts, {_settings.Keywords.Count} keywords");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ ERROR saving settings: {ex.Message}");
            }
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            // Se setup ainda não está completo, mostrar SetupControl
            if (!_setupComplete)
            {
                if (_setupControl == null)
                {
                    _setupControl = new UI.SetupControl();

                    // Subscribe ao evento de Retry
                    _setupControl.RetryRequested += OnSetupRetryRequested;

                    // Subscribe ao evento de Continue
                    _setupControl.ContinueRequested += OnSetupContinueRequested;
                }
                return _setupControl;
            }

            // Setup completo, mostrar SettingsControl normal
            if (_settingsControl == null)
            {
                _settingsControl = new UI.SettingsControl(this);
            }
            return _settingsControl;
        }

        // Métodos públicos para a UI
        public void DisconnectWhatsApp()
        {
            _nodeManager?.Stop();
        }

        public async System.Threading.Tasks.Task ReconnectWhatsApp()
        {
            WriteLog("Reconnecting WhatsApp...");

            try
            {
                // 🔥 Primeiro parar tudo completamente
                _nodeManager?.Stop();

                // Pequeno delay para garantir que tudo fechou
                await System.Threading.Tasks.Task.Delay(500);

                // Agora iniciar de novo
                WriteLog("Starting Node.js server for reconnection...");
                await _nodeManager.StartAsync();

                WriteLog("✅ Reconnection process completed");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ ERROR during reconnection: {ex.Message}");
                WriteLog($"   Stack trace: {ex.StackTrace}");
                _connectionStatus = "Error";
                _settingsControl?.UpdateConnectionStatus("Error");

                // NÃO mostrar MessageBox - só log!
            }
        }

        public async void RefreshChatContacts()
        {
            WriteLog("🔄 Refreshing chat contacts list...");

            try
            {
                if (_nodeManager != null)
                {
                    await _nodeManager.SendCommandAsync("refreshChatContacts");
                    WriteLog("✅ Refresh command sent to Node.js");
                }
                else
                {
                    WriteLog("❌ Cannot refresh: Node.js not connected");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error refreshing contacts: {ex.Message}");
            }
        }

        public void ApplyDisplaySettings()
        {
            // Recriar MessageQueue com novas configurações
            _messageQueue?.Dispose();
            _messageQueue = new MessageQueue(_settings, WriteLog);
            _messageQueue.OnGroupDisplay += MessageQueue_OnGroupDisplay;
            _messageQueue.OnMessageRemoved += MessageQueue_OnMessageRemoved;

            // Attach overlay ao VoCore selecionado
            AttachToVoCore();
        }

        /// <summary>
        /// Faz hook no VoCore para renderizar overlay ANTES do frame final
        /// </summary>
        private void AttachToVoCore()
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    WriteLog("No target device selected for overlay");
                    return;
                }

                // Obter todos os devices via reflection
                var getAllDevicesMethod = PluginManager.GetType().GetMethod("GetAllDevices");
                if (getAllDevicesMethod == null)
                {
                    WriteLog("ERROR: GetAllDevices method not found");
                    return;
                }

                // Chamar GetAllDevices(true) para incluir disabled devices
                var devicesEnumerable = getAllDevicesMethod.Invoke(PluginManager, new object[] { true }) as System.Collections.IEnumerable;
                if (devicesEnumerable == null)
                {
                    WriteLog("ERROR: GetAllDevices returned null");
                    return;
                }

                // Procurar o VoCore target
                foreach (var device in devicesEnumerable)
                {
                    var deviceType = device.GetType();

                    // Obter MainDisplayName para comparar
                    var mainNameProp = deviceType.GetProperty("MainDisplayName");
                    var instanceIdProp = deviceType.GetProperty("InstanceId");

                    var mainName = mainNameProp?.GetValue(device)?.ToString();
                    var instanceId = instanceIdProp?.GetValue(device)?.ToString();

                    // Verificar se é o device certo
                    bool isTargetDevice = (mainName == _settings.TargetDevice) ||
                                         (instanceId == _settings.TargetDevice);

                    if (!isTargetDevice)
                        continue;

                    // Tentar obter BitmapDisplayInstance
                    var bitmapProp = deviceType.GetProperty("BitmapDisplayInstance");
                    if (bitmapProp == null)
                    {
                        WriteLog("ERROR: BitmapDisplayInstance property not found");
                        return;
                    }

                    var bitmapInstance = bitmapProp.GetValue(device);
                    if (bitmapInstance == null)
                    {
                        WriteLog("ERROR: BitmapDisplayInstance is null");
                        return;
                    }

                    _vocoreDevice = bitmapInstance;

                    // Obter Settings do device
                    var settingsProp = deviceType.GetProperty("Settings");
                    if (settingsProp != null)
                    {
                        _vocoreSettings = settingsProp.GetValue(device);
                    }
                    else
                    {
                        WriteLog("WARNING: Could not get VoCore Settings");
                    }

                    // Attach renderer ao device
                    _overlayRenderer.AttachToDevice(bitmapInstance);

                    return;
                }

                WriteLog($"WARNING: Target device '{_settings.TargetDevice}' not found");
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR attaching to VoCore: {ex.Message}");
                WriteLog($"Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Ativa o overlay (liga information overlay + define dashboard)
        /// </summary>
        /// <summary>
        /// SIMPLES: Garante que overlay está ativo com dashboard correto
        /// Chama APENAS: 1) Ao iniciar 2) Quando muda device
        /// SÓ MUDA se não estiver correto!
        /// VERIFICA se pasta do dashboard existe (pode ter sido apagada)
        /// THROTTLE: Só verifica dashboard a cada 30 segundos
        /// </summary>
        private void EnsureOverlayActive()
        {
            if (_vocoreSettings == null)
            {
                return;
            }

            try
            {
                // 🔥 THROTTLE: Verificar dashboard apenas 1x a cada 30 segundos
                var timeSinceLastCheck = (DateTime.Now - _lastDashboardCheck).TotalSeconds;
                if (timeSinceLastCheck >= 30)
                {
                    _lastDashboardCheck = DateTime.Now;

                    // ✅ PASSO 0: Verificar se pasta do dashboard existe
                    var dashboardInstaller = new DashboardInstaller(PluginManager, WriteLog);
                    if (!dashboardInstaller.IsDashboardInstalled())
                    {
                        WriteLog("⚠️ Dashboard folder not found! Reinstalling...");
                        bool reinstalled = dashboardInstaller.InstallDashboard();

                        if (reinstalled)
                        {
                            WriteLog("✅ Dashboard reinstalled successfully");
                        }
                        else
                        {
                            WriteLog("❌ Failed to reinstall dashboard");
                            return;
                        }
                    }
                    // ✅ Pasta existe - não faz log (silencioso)
                }

                var settingsType = _vocoreSettings.GetType();

                // PASSO 1: Verificar se information overlay está ligado
                var useOverlayProp = settingsType.GetProperty("UseOverlayDashboard");
                if (useOverlayProp != null)
                {
                    var isActive = (bool)useOverlayProp.GetValue(_vocoreSettings);

                    if (!isActive)
                    {
                        // Ligar overlay - SÓ faz log quando muda!
                        useOverlayProp.SetValue(_vocoreSettings, true);
                        WriteLog("✅ Information overlay activated");
                    }
                    // ✅ Já está ligado - não faz log (silencioso)
                }

                // PASSO 2: Verificar se dashboard está correto
                var overlayDashboardProp = settingsType.GetProperty("CurrentOverlayDashboard");
                if (overlayDashboardProp != null)
                {
                    var overlayDashboard = overlayDashboardProp.GetValue(_vocoreSettings);
                    if (overlayDashboard != null)
                    {
                        // Verificar dashboard atual
                        var getCurrentMethod = overlayDashboard.GetType().GetMethod("Get");
                        string currentDashboard = null;

                        if (getCurrentMethod != null)
                        {
                            currentDashboard = getCurrentMethod.Invoke(overlayDashboard, null) as string;
                        }

                        // Só mudar se não for WhatsAppPlugin
                        if (currentDashboard != "WhatsAppPlugin")
                        {
                            var trySetMethod = overlayDashboard.GetType().GetMethod("TrySet");
                            if (trySetMethod != null)
                            {
                                trySetMethod.Invoke(overlayDashboard, new object[] { "WhatsAppPlugin" });
                                WriteLog($"✅ Dashboard changed: {currentDashboard ?? "none"} → WhatsAppPlugin");
                            }
                        }
                        // ✅ Já está correto - não faz log (silencioso)
                    }
                }
                // ✅ Tudo OK - não faz log "Overlay already configured" (silencioso)
            }
            catch (Exception ex)
            {
                WriteLog($"⚠️ EnsureOverlayActive error: {ex.Message}");
            }
        }

        public async void TestQuickReply(int replyNumber, string text)
        {
            try
            {
                // Send test reply via WebSocket
                var chatId = _connectedNumber + "@c.us";
                await _nodeManager.SendReplyAsync(chatId, text);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Timer: Verifica de 30 em 30s se dashboard existe e reinstala se necessário
        /// </summary>
        private void DashboardCheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // Verificar se dashboard ainda existe
                if (_dashboardInstaller == null) return;

                bool exists = _dashboardInstaller.IsDashboardInstalled();

                if (!exists)
                {
                    // Dashboard foi apagado! Reinstalar automaticamente
                    WriteLog("⚠️ Dashboard not found! Auto-reinstalling...");

                    bool reinstalled = _dashboardInstaller.InstallDashboard();

                    if (reinstalled)
                    {
                        WriteLog("✅ Dashboard auto-reinstalled successfully!");
                    }
                    else
                    {
                        WriteLog("❌ Failed to auto-reinstall dashboard");
                    }
                }

                // ⭐ VERIFICAR SE OVERLAY ESTÁ ATIVO (a cada 30s)
                if (_vocoreDevice != null && _vocoreSettings != null)
                {
                    EnsureOverlayActive();
                }
            }
            catch (Exception ex)
            {
                WriteLog($"❌ DashboardCheckTimer error: {ex.Message}");
            }
        }

        /// <summary>
        /// 🎮 Método chamado automaticamente pelo SimHub a 60 FPS!
        ///
        /// ✅ QUICK REPLIES: Sistema NATIVO do SimHub com ControlsEditor + Actions!
        ///
        /// O ControlsEditor liga automaticamente os botões às Actions registadas.
        /// Quando o user carrega no botão, o SimHub chama a Action diretamente.
        /// Não é necessário verificar nada aqui!
        /// </summary>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Quick replies funcionam via Actions - não precisa de código aqui!
            // Ver RegisterActions() onde as Actions são definidas
        }

        /// <summary>
        /// 📤 Envia quick reply via Node.js
        /// </summary>
        private async void SendQuickReply(QueuedMessage message, string replyText)
        {
            try
            {
                if (_nodeManager == null || !_nodeManager.IsConnected)
                {
                    WriteLog("❌ Cannot send reply: Node.js not connected!");
                    _overlayRenderer?.SetSystemMessage("❌ WhatsApp not connected\nCannot send reply");
                    return;
                }

                WriteLog($"📤 Sending quick reply to {message.From}...");
                WriteLog($"   Chat ID: {message.ChatId}");
                WriteLog($"   Text: {replyText}");

                // Criar comando para Node.js
                var command = new
                {
                    type = "sendReply",
                    chatId = message.ChatId,
                    text = replyText
                };

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(command);
                await _nodeManager.SendCommandAsync(json);

                WriteLog($"✅ Quick reply sent to {message.From}!");

                // Mostrar confirmação no overlay
                _overlayRenderer?.SetSystemMessage($"✅ Reply sent to\n{message.From}");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error sending reply: {ex.Message}");
                WriteLog($"   Stack: {ex.StackTrace}");
                _overlayRenderer?.SetSystemMessage($"❌ Error sending reply\n{ex.Message}");
            }
        }


        /// <summary>
        /// Mostra mensagem de teste por 5 segundos (não muda VoCore ou dashboard)
        /// Durante o teste, ignora completamente as 2 queues
        /// Ao fim dos 5s, LIMPA TUDO para o plugin poder continuar
        /// </summary>
        public void ShowTestMessage()
        {
            try
            {
                WriteLog($"[TEST] ▶ ShowTestMessage started");

                // 🔥 BLOQUEAR QUEUES durante teste
                _isTestingMessage = true;
                WriteLog($"[TEST] _isTestingMessage = TRUE (queues BLOCKED)");

                // Hora atual formatada
                string currentTime = DateTime.Now.ToString("HH:mm");

                // ✅ Definir campos privados diretamente (expostos via AttachDelegate)
                _showMessage = true;
                _overlaySender = "Bruno Silva";
                _overlayTypeMessage = "VIP"; // Badge estrela
                _overlayTotalMessages = 1;
                _overlayMessages[0] = $"{currentTime} Ola isto é um teste :)";
                _overlayMessages[1] = "";
                _overlayMessages[2] = "";
                _overlayMessages[3] = "";
                _overlayMessages[4] = "";

                WriteLog($"✅ Test message displayed: {currentTime} Ola isto é um teste :)");
                WriteLog($"[TEST] Waiting 5 seconds before clearing...");

                // 🔥 Após 5 segundos: LIMPAR TUDO e DESBLOQUEAR QUEUES
                System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                {
                    WriteLog($"[TEST] ▶ 5 seconds elapsed - clearing test message");

                    // Limpar TUDO para o overlay desaparecer
                    _showMessage = false;
                    _overlaySender = "";
                    _overlayTypeMessage = "";
                    _overlayTotalMessages = 0;
                    _overlayMessages[0] = "";
                    _overlayMessages[1] = "";
                    _overlayMessages[2] = "";
                    _overlayMessages[3] = "";
                    _overlayMessages[4] = "";

                    WriteLog($"[TEST] Overlay properties cleared");

                    // Desbloquear queues
                    _isTestingMessage = false;
                    WriteLog($"[TEST] _isTestingMessage = FALSE (queues UNBLOCKED)");

                    WriteLog("✅ Test message cleared after 5 seconds");

                    // ✅ REPROCESSAR FILA (se houver mensagens pendentes)
                    if (_messageQueue != null)
                    {
                        WriteLog($"[TEST] Scheduling ProcessQueue in 100ms...");
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            System.Threading.Thread.Sleep(100); // Pequeno delay
                            WriteLog($"[TEST] Calling TriggerProcessQueue()...");
                            _messageQueue?.TriggerProcessQueue();
                            WriteLog($"[TEST] ✅ TriggerProcessQueue() completed");
                        });
                    }
                    else
                    {
                        WriteLog($"[TEST] ⚠ _messageQueue is null - cannot reprocess");
                    }
                });
            }
            catch (Exception ex)
            {
                _isTestingMessage = false; // Garantir que desbloqueia em caso de erro
                WriteLog($"❌ ShowTestMessage error: {ex.Message}");
            }
        }

        /// <summary>
        /// Mostra confirmação de que quick reply foi enviada (5s, pausa queue)
        /// </summary>
        public void ShowQuickReplyConfirmation(string recipientName)
        {
            try
            {
                WriteLog($"[CONFIRMATION] ▶ Showing quick reply confirmation for {recipientName}");

                // 🔥 BLOQUEAR QUEUES durante confirmação
                _isTestingMessage = true;

                // Hora atual formatada
                string currentTime = DateTime.Now.ToString("HH:mm");

                // ✅ Mostrar confirmação
                _showMessage = true;
                _overlaySender = recipientName;
                _overlayTypeMessage = ""; // Sem badge
                _overlayTotalMessages = 1;
                _overlayMessages[0] = $"{currentTime} Quick reply enviada com sucesso";
                _overlayMessages[1] = "";
                _overlayMessages[2] = "";
                _overlayMessages[3] = "";
                _overlayMessages[4] = "";

                WriteLog($"[CONFIRMATION] ✅ Confirmation displayed for {recipientName}");

                // 🔥 Após 5 segundos: LIMPAR e DESBLOQUEAR
                System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                {
                    WriteLog($"[CONFIRMATION] ▶ 5 seconds elapsed - clearing confirmation");

                    // Limpar overlay
                    _showMessage = false;
                    _overlaySender = "";
                    _overlayTypeMessage = "";
                    _overlayTotalMessages = 0;
                    _overlayMessages[0] = "";
                    _overlayMessages[1] = "";
                    _overlayMessages[2] = "";
                    _overlayMessages[3] = "";
                    _overlayMessages[4] = "";

                    // Desbloquear queues
                    _isTestingMessage = false;
                    WriteLog($"[CONFIRMATION] _isTestingMessage = FALSE (queues UNBLOCKED)");

                    // ✅ REPROCESSAR FILA
                    if (_messageQueue != null)
                    {
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            System.Threading.Thread.Sleep(100);
                            _messageQueue?.TriggerProcessQueue();
                            WriteLog($"[CONFIRMATION] ✅ Queue resumed");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                _isTestingMessage = false; // Garantir que desbloqueia
                WriteLog($"[CONFIRMATION ERROR] {ex.Message}");
            }
        }

        /// <summary>
        /// Limpa todas as mensagens VIP/URGENT da queue
        /// Útil quando user ativa RemoveAfterFirstDisplay
        /// </summary>
        public void ClearVipUrgentQueue()
        {
            try
            {
                _messageQueue?.ClearVipUrgentMessages();
                WriteLog("[QUEUE] ✅ VIP/URGENT queue cleared");
            }
            catch (Exception ex)
            {
                WriteLog($"[QUEUE] ❌ ERROR clearing VIP/URGENT queue: {ex.Message}");
            }
        }

        /// <summary>
        /// Testa o sistema de overlay com dashboard .simhubdash
        /// </summary>
        public void TestDashboardOverlay()
        {
            try
            {
                WriteLog("");
                WriteLog("╔═══════════════════════════════════════════════════════════════════╗");
                WriteLog("║             TESTING DASHBOARD OVERLAY SYSTEM                      ║");
                WriteLog("╚═══════════════════════════════════════════════════════════════════╝");
                WriteLog("");

                if (_vocoreDevice == null)
                {
                    WriteLog("❌ ERROR: VoCore device not attached!");
                    WriteLog("   Please select a VoCore in settings.");
                    WriteLog("   Attempting to attach now...");

                    AttachToVoCore();

                    if (_vocoreDevice == null)
                    {
                        WriteLog("❌ FAILED: Could not attach to VoCore");
                        return;
                    }

                    WriteLog("✅ SUCCESS: Attached to VoCore!");
                }

                if (_vocoreSettings == null)
                {
                    WriteLog("❌ ERROR: VoCore settings not found!");
                    return;
                }

                // Criar mensagem de teste
                var testMessage = new QueuedMessage
                {
                    From = "🚨 TESTE WHATSAPP 🚨",
                    Number = "+351912345678",
                    Body = "SE VÊS ISTO, FUNCIONOU!\n\nDashboard overlay a funcionar!",
                    Timestamp = DateTime.Now,
                    IsVip = false,
                    IsUrgent = true
                };

                WriteLog("📝 Test message created");
                WriteLog($"   From: {testMessage.From}");
                WriteLog($"   Message: {testMessage.Body}");
                WriteLog("");

                // Mostrar overlay
                WriteLog("🎨 Calling ShowMessage()...");
                bool success = _overlayRenderer.ShowMessage(testMessage, WriteLog);

                if (success)
                {
                    WriteLog("");
                    WriteLog("✅ SUCCESS! Overlay should be visible now!");
                    WriteLog("");
                    WriteLog("╔═══════════════════════════════════════════════════════╗");
                    WriteLog("║   🎉 OVERLAY IS NOW ACTIVE!                          ║");
                    WriteLog("║      Check your VoCore - toggle should be ON!        ║");
                    WriteLog("║                                                       ║");
                    WriteLog("║   💡 Overlay will stay ON (not clearing)             ║");
                    WriteLog("╚═══════════════════════════════════════════════════════╝");
                    WriteLog("");
                    WriteLog("✅ Test completed!");

                    // NÃO desligar automaticamente!
                    // O overlay fica LIGADO para Bruno verificar!
                    // _overlayRenderer.ClearOverlay(WriteLog);
                }
                else
                {
                    WriteLog("❌ FAILED: Could not show overlay");
                }

                WriteLog("");
                WriteLog("╔═══════════════════════════════════════════════════════════════════╗");
                WriteLog("║                    TEST COMPLETED                                 ║");
                WriteLog("╚═══════════════════════════════════════════════════════════════════╝");
                WriteLog("");

            }
            catch (Exception ex)
            {
                WriteLog($"❌ TestDashboardOverlay ERROR: {ex.Message}");
                WriteLog($"   Stack: {ex.StackTrace}");
            }
        }

        #region Dependency Setup

        /// <summary>
        /// Inicializa e verifica todas as dependências (Node.js, Git, npm packages)
        /// Só arranca Node.js depois de tudo instalado
        /// </summary>
        private async Task InitializeDependenciesAsync()
        {
            try
            {
                _dependencyManager = new DependencyManager(_pluginPath);
                _dependencyManager.StatusChanged += (s, msg) => WriteLog(msg);

                // GARANTIR que SetupControl está pronto antes de começar
                WriteLog("Waiting for Setup UI to initialize...");
                int retries = 0;
                while (_setupControl == null && retries < 30)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    retries++;
                }

                if (_setupControl != null)
                {
                    WriteLog("✅ Setup UI ready! Initializing status...");

                    // INICIALIZAR TODOS OS STATUS EXPLICITAMENTE
                    _setupControl.UpdateNodeStatus("Checking...", false);
                    _setupControl.UpdateGitStatus("Waiting...", false);
                    _setupControl.UpdateNpmStatus("Waiting...", false);
                    _setupControl.UpdateProgress(0, "Checking dependencies...");

                    // Pequeno delay para UI renderizar
                    await Task.Delay(200).ConfigureAwait(false);
                }
                else
                {
                    WriteLog("⚠️ WARNING: Setup UI not available after 3 seconds!");
                }

                WriteLog("🔍 Checking Node.js...");

                // ============ NODE.JS ============
                WriteLog("Checking if Node.js is installed (portable or global)...");
                bool nodeInstalled = _dependencyManager.IsNodeInstalled();
                WriteLog($"Node.js check result: {nodeInstalled}");

                if (!nodeInstalled)
                {
                    WriteLog("⚠️ Node.js not found - installing automatically...");

                    if (_setupControl != null)
                    {
                        _setupControl.UpdateNodeStatus("Installing Node.js portable...", false);
                        _setupControl.UpdateProgress(10, "Installing Node.js...");
                    }

                    bool success = await _dependencyManager.InstallNodeSilently().ConfigureAwait(false);

                    if (success)
                    {
                        WriteLog("✅ Node.js portable installed!");

                        // Aguardar 500ms para filesystem atualizar
                        await Task.Delay(500).ConfigureAwait(false);

                        // VERIFICAR se foi instalado
                        WriteLog("Verifying Node.js installation...");
                        bool verifyInstalled = _dependencyManager.IsNodeInstalled();

                        if (verifyInstalled)
                        {
                            WriteLog("✅ Node.js files verified!");

                            // TESTAR EXECUÇÃO REAL E CAPTURAR VERSÃO!
                            WriteLog("Testing Node.js execution...");
                            var (canExecute, nodeVersion) = await TestNodeExecutionAsync().ConfigureAwait(false);

                            if (canExecute && !string.IsNullOrEmpty(nodeVersion))
                            {
                                WriteLog($"✅ Node.js is executable and ready! Version: {nodeVersion}");
                                if (_setupControl != null)
                                {
                                    _setupControl.UpdateNodeStatus($"Installed ({nodeVersion})", true);
                                    _setupControl.UpdateProgress(33, "Node.js ready!");
                                }
                            }
                            else
                            {
                                WriteLog("⚠️ WARNING: Node.js installed but cannot execute - may need PATH refresh");
                                if (_setupControl != null)
                                {
                                    _setupControl.UpdateNodeStatus("Installed (PATH pending)", true);
                                    _setupControl.UpdateProgress(33, "Node.js installed!");
                                }
                            }
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Node.js installed but verification failed");
                            if (_setupControl != null)
                            {
                                _setupControl.UpdateNodeStatus("Installed (verification pending)", true);
                                _setupControl.UpdateProgress(33, "Node.js installed!");
                            }
                        }
                    }
                    else
                    {
                        WriteLog("❌ ERROR: Failed to install Node.js!");
                        if (_setupControl != null)
                        {
                            _setupControl.UpdateNodeStatus("Installation failed", false, true);
                            _setupControl.UpdateProgress(0, "ERROR: Node.js installation failed");
                            _setupControl.ShowRetryButton(); // MOSTRAR BOTÃO RETRY!
                        }
                        return;
                    }
                }
                else
                {
                    WriteLog("✅ Node.js already installed (found existing installation)!");
                    WriteLog("This could be: portable local, global, or in PATH");
                    WriteLog("No need to install - will use existing Node.js");

                    // TESTAR se executa E CAPTURAR VERSÃO!
                    WriteLog("Testing existing Node.js execution...");
                    var (canExecute, nodeVersion) = await TestNodeExecutionAsync().ConfigureAwait(false);

                    if (_setupControl != null)
                    {
                        if (canExecute && !string.IsNullOrEmpty(nodeVersion))
                        {
                            WriteLog($"Updating UI: Node.js status = Installed ({nodeVersion})");
                            _setupControl.UpdateNodeStatus($"Installed ({nodeVersion})", true);
                            _setupControl.UpdateProgress(33, "Node.js ready!");
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Node.js found but cannot execute!");
                            _setupControl.UpdateNodeStatus("Found (cannot execute)", true);
                            _setupControl.UpdateProgress(33, "Node.js found!");
                        }
                        WriteLog("UI updated successfully!");

                        // Delay para garantir que UI renderiza
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                    else
                    {
                        WriteLog("❌ ERROR: _setupControl is NULL! Cannot update UI!");
                    }
                }

                WriteLog("Node.js check complete! Moving to Git...");

                // ============ GIT ============
                WriteLog("🔍 Checking Git...");

                bool gitInstalled = _dependencyManager.IsGitInstalled();

                if (!gitInstalled)
                {
                    WriteLog("⚠️ Git not found - installing automatically...");

                    if (_setupControl != null)
                    {
                        _setupControl.UpdateGitStatus("Installing Git...", false);
                        _setupControl.UpdateProgress(40, "Installing Git...");
                    }

                    bool success = await _dependencyManager.InstallGitSilently().ConfigureAwait(false);

                    if (success)
                    {
                        WriteLog("✅ Git portable installed!");

                        // Aguardar 500ms para filesystem atualizar
                        await Task.Delay(500).ConfigureAwait(false);

                        // VERIFICAR se foi instalado
                        WriteLog("Verifying Git installation...");
                        bool verifyInstalled = _dependencyManager.IsGitInstalled();

                        if (verifyInstalled)
                        {
                            WriteLog("✅ Git files verified!");

                            // TESTAR EXECUÇÃO REAL E CAPTURAR VERSÃO!
                            WriteLog("Testing Git execution...");
                            var (canExecute, gitVersion) = await TestGitExecutionAsync().ConfigureAwait(false);

                            if (canExecute && !string.IsNullOrEmpty(gitVersion))
                            {
                                WriteLog($"✅ Git is executable and ready! Version: {gitVersion}");
                                if (_setupControl != null)
                                {
                                    _setupControl.UpdateGitStatus($"Installed ({gitVersion})", true);
                                    _setupControl.UpdateProgress(66, "Git ready!");
                                }
                            }
                            else
                            {
                                WriteLog("⚠️ WARNING: Git installed but cannot execute - may need PATH refresh");
                                if (_setupControl != null)
                                {
                                    _setupControl.UpdateGitStatus("Installed (PATH pending)", true);
                                    _setupControl.UpdateProgress(66, "Git installed!");
                                }
                            }
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Git installed but verification failed");
                            if (_setupControl != null)
                            {
                                _setupControl.UpdateGitStatus("Installed (verification pending)", true);
                                _setupControl.UpdateProgress(66, "Git installed!");
                            }
                        }
                    }
                    else
                    {
                        WriteLog("❌ ERROR: Failed to install Git!");
                        if (_setupControl != null)
                        {
                            _setupControl.UpdateGitStatus("Installation failed", false, true);
                            _setupControl.UpdateProgress(0, "ERROR: Git installation failed");
                            _setupControl.ShowRetryButton(); // MOSTRAR BOTÃO RETRY!
                        }
                        return;
                    }
                }
                else
                {
                    WriteLog("✅ Git already installed (found existing installation)!");

                    // TESTAR se executa E CAPTURAR VERSÃO!
                    WriteLog("Testing existing Git execution...");
                    var (canExecute, gitVersion) = await TestGitExecutionAsync().ConfigureAwait(false);

                    if (_setupControl != null)
                    {
                        if (canExecute && !string.IsNullOrEmpty(gitVersion))
                        {
                            WriteLog($"Updating UI: Git status = Installed ({gitVersion})");
                            _setupControl.UpdateGitStatus($"Installed ({gitVersion})", true);
                            _setupControl.UpdateProgress(66, "Git ready!");
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Git found but cannot execute!");
                            _setupControl.UpdateGitStatus("Found (cannot execute)", true);
                            _setupControl.UpdateProgress(66, "Git found!");
                        }
                    }
                }

                // ============ NPM PACKAGES ============
                WriteLog("🔍 Checking npm packages...");

                bool packagesInstalled = _dependencyManager.AreNpmPackagesInstalled();

                if (!packagesInstalled)
                {
                    WriteLog("⚠️ npm packages not found - installing...");

                    if (_setupControl != null)
                    {
                        _setupControl.UpdateNpmStatus("Installing packages (this may take 1-2 minutes)...", false);
                        _setupControl.UpdateProgress(70, "Installing npm packages...");
                    }

                    bool success = await _dependencyManager.InstallNpmPackages().ConfigureAwait(false);

                    if (success)
                    {
                        WriteLog("✅ npm packages installed successfully!");
                        if (_setupControl != null)
                        {
                            _setupControl.UpdateNpmStatus("Installed (whatsapp-web.js + dependencies)", true);
                            _setupControl.UpdateProgress(100, "All dependencies ready!");
                        }
                    }
                    else
                    {
                        WriteLog("❌ ERROR: Failed to install npm packages!");
                        if (_setupControl != null)
                        {
                            _setupControl.UpdateNpmStatus("Installation failed", false, true);
                            _setupControl.UpdateProgress(0, "ERROR: npm install failed");
                            _setupControl.ShowRetryButton(); // MOSTRAR BOTÃO RETRY!
                        }
                        return;
                    }
                }
                else
                {
                    WriteLog("✅ npm packages already installed");
                    if (_setupControl != null)
                    {
                        _setupControl.UpdateNpmStatus("Already installed", true);
                        _setupControl.UpdateProgress(100, "All dependencies ready!");
                    }
                }

                // ============ TUDO PRONTO! ============
                WriteLog("✅ All dependencies installed - starting Node.js...");
                _setupComplete = true;

                // SALVAR FLAG DE SETUP COMPLETO (persiste entre restarts!)
                try
                {
                    string setupFlagPath = Path.Combine(_pluginPath, ".setup-complete");
                    File.WriteAllText(setupFlagPath, DateTime.Now.ToString());
                    WriteLog($"✅ Setup flag saved: {setupFlagPath}");
                }
                catch (Exception ex)
                {
                    WriteLog($"⚠️ Could not save setup flag: {ex.Message}");
                }

                // Mostrar botão Continue!
                if (_setupControl != null)
                {
                    _setupControl.ShowContinueButton();
                }

                // Aguardar 1s para user ver a UI completa
                await Task.Delay(1000).ConfigureAwait(false);

                // Agora sim, arrancar Node.js!
                await StartNodeJs().ConfigureAwait(false);

                // Tentar anexar ao VoCore se já configurado
                if (!string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    AttachToVoCore();

                    // Auto-ativar overlay
                    if (_vocoreDevice != null)
                    {
                        WriteLog("🎯 Auto-activating overlay...");
                        await Task.Delay(1000).ConfigureAwait(false);
                        EnsureOverlayActive();
                    }
                }

                WriteLog("🎉 Plugin ready to use!");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ CRITICAL ERROR during dependency setup: {ex.Message}");
                WriteLog($"   Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Arranca Node.js (só chamado depois de dependências instaladas)
        /// </summary>
        private async Task StartNodeJs()
        {
            try
            {
                WriteLog("🚀 Starting Node.js...");
                await _nodeManager.StartAsync().ConfigureAwait(false);
                WriteLog("✅ Node.js started successfully!");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Failed to start Node.js: {ex.Message}");
                WriteLog($"   Stack trace: {ex.StackTrace}");
                _connectionStatus = "Error";
                _settingsControl?.UpdateConnectionStatus("Error");
            }
        }

        /// <summary>
        /// Testa se Node.js pode ser executado e captura a versão
        /// </summary>
        /// <returns>(success, version)</returns>
        private async Task<(bool success, string version)> TestNodeExecutionAsync()
        {
            try
            {
                WriteLog("Testing if 'node --version' executes...");

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "node",
                                Arguments = "--version",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                        await Task.Run(() => process.WaitForExit(5000)).ConfigureAwait(false);

                        if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                        {
                            string version = output.Trim(); // ex: v20.11.0
                            WriteLog($"✅ Node.js executes successfully! Version: {version}");
                            return (true, version);
                        }

                        WriteLog($"⚠️ Attempt {attempt}/3: Node execution failed (exit code: {process.ExitCode})");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"⚠️ Attempt {attempt}/3: Cannot execute node - {ex.Message}");
                    }

                    // Aguardar antes de retry
                    if (attempt < 3)
                    {
                        WriteLog($"Waiting 1 second before retry...");
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                }

                WriteLog("❌ Node.js cannot be executed after 3 attempts");
                return (false, null);
            }
            catch (Exception ex)
            {
                WriteLog($"❌ ERROR testing Node.js execution: {ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// Testa se Git pode ser executado e captura a versão
        /// </summary>
        /// <returns>(success, version)</returns>
        private async Task<(bool success, string version)> TestGitExecutionAsync()
        {
            try
            {
                WriteLog("Testing if 'git --version' executes...");

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "git",
                                Arguments = "--version",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                        await Task.Run(() => process.WaitForExit(5000)).ConfigureAwait(false);

                        if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                        {
                            // Extrair versão: "git version 2.47.1.windows.1" → "2.47.1"
                            string version = output.Trim();
                            if (version.Contains("version"))
                            {
                                var parts = version.Split(' ');
                                if (parts.Length >= 3)
                                {
                                    version = parts[2].Split('.')[0] + "." + parts[2].Split('.')[1] + "." + parts[2].Split('.')[2];
                                }
                            }
                            WriteLog($"✅ Git executes successfully! Version: {version}");
                            return (true, version);
                        }

                        WriteLog($"⚠️ Attempt {attempt}/3: Git execution failed (exit code: {process.ExitCode})");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"⚠️ Attempt {attempt}/3: Cannot execute git - {ex.Message}");
                    }

                    // Aguardar antes de retry
                    if (attempt < 3)
                    {
                        WriteLog($"Waiting 1 second before retry...");
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                }

                WriteLog("❌ Git cannot be executed after 3 attempts");
                return (false, null);
            }
            catch (Exception ex)
            {
                WriteLog($"❌ ERROR testing Git execution: {ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// Event handler quando user clica no botão Retry do Setup
        /// </summary>
        private void OnSetupRetryRequested(object sender, EventArgs e)
        {
            WriteLog("🔄 User requested setup retry - restarting dependency installation...");

            // Reset states

            // Esconder o botão
            _setupControl?.HideRetryButton();

            // Resetar UI
            if (_setupControl != null)
            {
                _setupControl.UpdateNodeStatus("Retrying...", false);
                _setupControl.UpdateGitStatus("Waiting...", false);
                _setupControl.UpdateNpmStatus("Waiting...", false);
                _setupControl.UpdateProgress(0, "Retrying setup...");
            }

            // TENTAR NOVAMENTE TUDO!
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500).ConfigureAwait(false); // Pequeno delay antes de começar
                    await InitializeDependenciesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    WriteLog($"❌ ERROR during retry: {ex.Message}");
                    WriteLog($"   Stack: {ex.StackTrace}");
                }
            });
        }

        /// <summary>
        /// Event handler quando user clica no botão Restart SimHub
        /// </summary>
        private void OnSetupContinueRequested(object sender, EventArgs e)
        {
            WriteLog("🔄 User clicked Restart SimHub - finalizing setup...");

            // Marcar setup como completo!
            _setupComplete = true;

            // 🔥 CRIAR ARQUIVO FLAG PARA PERSISTIR ENTRE SESSÕES!
            try
            {
                string setupFlagPath = Path.Combine(_pluginPath, ".setup-complete");
                File.WriteAllText(setupFlagPath, DateTime.Now.ToString());
                WriteLog($"✅ Created setup flag file: {setupFlagPath}");
            }
            catch (Exception ex)
            {
                WriteLog($"⚠️ Could not create setup flag file: {ex.Message}");
            }

            // Esconder botão e mostrar mensagem de restart
            if (_setupControl != null)
            {
                _setupControl.Dispatcher.Invoke(() =>
                {
                    // Esconder botão
                    _setupControl.HideContinueButton();

                    // Mostrar mensagem de restart
                    _setupControl.UpdateProgress(100,
                        "🔄 Setup complete!\n\n" +
                        "SimHub will restart in 3 seconds...\n" +
                        "When it reopens, the main WhatsApp interface will appear.");
                });
            }

            WriteLog("✅ Setup complete. Preparing to restart SimHub...");

            // 🔄 RESTART SIMHUB AUTOMATICAMENTE!
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // Aguardar 3 segundos para user ver mensagem
                    await System.Threading.Tasks.Task.Delay(3000);

                    WriteLog("🔄 Cleaning up processes before restart...");

                    // 🔥 MATAR PROCESSOS CHROME (puppeteer do whatsapp-web.js)
                    try
                    {
                        var chromeProcesses = System.Diagnostics.Process.GetProcessesByName("chrome");
                        int killedCount = 0;

                        foreach (var proc in chromeProcesses)
                        {
                            try
                            {
                                var cmdLine = GetProcessCommandLine(proc);
                                if (cmdLine != null &&
                                    (cmdLine.IndexOf("WhatsAppPlugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     cmdLine.IndexOf("puppeteer", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    WriteLog($"  Killing Chrome process {proc.Id}");
                                    proc.Kill();
                                    proc.WaitForExit(1000);
                                    killedCount++;
                                }
                            }
                            catch { /* Ignore */ }
                        }

                        if (killedCount > 0)
                            WriteLog($"✅ Killed {killedCount} Chrome process(es)");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"⚠️ Could not kill Chrome processes: {ex.Message}");
                    }

                    // 🔥 MATAR PROCESSOS NODE.JS
                    try
                    {
                        if (_nodeManager != null)
                        {
                            WriteLog("  Stopping Node.js manager...");
                            _nodeManager.Stop();
                        }

                        var nodeProcesses = System.Diagnostics.Process.GetProcessesByName("node");
                        int killedCount = 0;

                        foreach (var proc in nodeProcesses)
                        {
                            try
                            {
                                var cmdLine = GetProcessCommandLine(proc);
                                if (cmdLine != null && cmdLine.IndexOf("whatsapp-client.js", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    WriteLog($"  Killing Node.js process {proc.Id}");
                                    proc.Kill();
                                    proc.WaitForExit(1000);
                                    killedCount++;
                                }
                            }
                            catch { /* Ignore */ }
                        }

                        if (killedCount > 0)
                            WriteLog($"✅ Killed {killedCount} Node.js process(es)");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"⚠️ Could not kill Node.js processes: {ex.Message}");
                    }

                    WriteLog("✅ Processes cleaned up. Restarting SimHub...");

                    // 🔄 USAR MÉTODO RESTART DO SIMHUB (como Lovely plugin)
                    try
                    {
                        // Tentar RestartApplication primeiro
                        var restartMethod = PluginManager.GetType().GetMethod("RestartApplication");
                        if (restartMethod != null)
                        {
                            WriteLog("🔄 Using PluginManager.RestartApplication() - SIMHUB WILL RESTART!");
                            restartMethod.Invoke(PluginManager, null);
                            return; // Se funcionou, acabou!
                        }

                        // Tentar Restart se RestartApplication não existir
                        restartMethod = PluginManager.GetType().GetMethod("Restart");
                        if (restartMethod != null)
                        {
                            WriteLog("🔄 Using PluginManager.Restart() - SIMHUB WILL RESTART!");
                            restartMethod.Invoke(PluginManager, null);
                            return;
                        }

                        WriteLog("⚠️ No restart method found in PluginManager, using fallback...");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"⚠️ Could not use PluginManager restart: {ex.Message}");
                    }

                    // FALLBACK: Restart manual
                    WriteLog("🔄 Using fallback: Process.Start + Exit");
                    var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                    string simHubPath = currentProcess.MainModule.FileName;

                    WriteLog($"🔄 Starting new SimHub from: {simHubPath}");
                    System.Diagnostics.Process.Start(simHubPath);

                    await System.Threading.Tasks.Task.Delay(500);

                    WriteLog("🔄 Closing current SimHub instance...");
                    System.Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    WriteLog($"❌ ERROR restarting SimHub: {ex.Message}");
                    WriteLog($"   Stack: {ex.StackTrace}");

                    // Fallback: mostrar mensagem para user fazer manualmente
                    if (_setupControl != null)
                    {
                        _setupControl.Dispatcher.Invoke(() =>
                        {
                            _setupControl.UpdateProgress(100,
                                "⚠️ Could not restart automatically.\n\n" +
                                "Please close and reopen SimHub manually.\n" +
                                "The main WhatsApp interface will then appear.");
                        });
                    }
                }
            });
        }


        #endregion
    }
}
