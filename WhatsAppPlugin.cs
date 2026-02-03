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
        private object _vocoreDevice; // Reference to VoCore BitmapDisplayDevice
        private object _vocoreSettings; // VoCore settings
        private DateTime _lastDashboardCheck = DateTime.MinValue; // Throttle dashboard verification
        private bool _isTestingMessage = false; // Flag to block queues during test

        private DashboardInstaller _dashboardInstaller; // Installer to reinstall dashboard
        private DashboardMerger _dashboardMerger; // Merger to combine dashboards

        // QUICK REPLIES: Now work via registered Actions
        // See RegisterActions() and SendQuickReply(int)
        private bool _replySentForCurrentMessage = false; // Blocks multiple sends for same message

        private string _pluginPath;
        private string _settingsFile;
        private string _contactsFile;
        private string _keywordsFile;
        private UI.SettingsControl _settingsControl;

        // SETUP & DEPENDENCIES
        private DependencyManager _dependencyManager;
        // SetupControl removed - dependencies now managed in SettingsControl Connection tab

        // Public property for settings access
        public PluginSettings Settings => _settings;

        // Property to check if Node.js script is running
        public bool IsScriptRunning => _nodeManager?.IsConnected ?? false;

        // Check if Node.js is installed (cached to avoid blocking)
        private bool? _nodeJsInstalledCache = null;
        private DateTime _nodeJsCacheTime = DateTime.MinValue;
        private bool _nodeJsCheckInProgress = false;

        public bool IsNodeJsInstalled()
        {
            // Use cache for 30 seconds to avoid repeated verifications
            if (_nodeJsInstalledCache.HasValue && (DateTime.Now - _nodeJsCacheTime).TotalSeconds < 30)
                return _nodeJsInstalledCache.Value;

            // Check if node.exe exists in common locations (non-blocking)
            var nodePaths = new[]
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe"
            };

            foreach (var path in nodePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    _nodeJsInstalledCache = true;
                    _nodeJsCacheTime = DateTime.Now;
                    return true;
                }
            }

            // If we have cache (expired), return old value and check in background
            if (_nodeJsInstalledCache.HasValue)
            {
                if (!_nodeJsCheckInProgress)
                {
                    _nodeJsCheckInProgress = true;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            bool result = CheckNodeJsViaProcess();
                            _nodeJsInstalledCache = result;
                            _nodeJsCacheTime = DateTime.Now;
                        }
                        finally
                        {
                            _nodeJsCheckInProgress = false;
                        }
                    });
                }
                return _nodeJsInstalledCache.Value;
            }

            // First time - do synchronous check (unavoidable)
            bool firstResult = CheckNodeJsViaProcess();
            _nodeJsInstalledCache = firstResult;
            _nodeJsCacheTime = DateTime.Now;
            return firstResult;
        }

        /// <summary>
        /// Checks Node.js via process (may block up to 1.5s)
        /// </summary>
        private bool CheckNodeJsViaProcess()
        {
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
                bool completed = proc.WaitForExit(1500);
                return completed && proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create BLACK AND WHITE WhatsApp icon for SimHub menu
        /// </summary>
        private ImageSource CreateWhatsAppIcon()
        {
            try
            {
                var drawingGroup = new DrawingGroup();

                // Outer circle (BLACK)
                var circlePen = new Pen(Brushes.Black, 2.5);
                drawingGroup.Children.Add(new GeometryDrawing(null, circlePen,
                    new EllipseGeometry(new Point(16, 16), 14, 14)));

                // Phone + bubble (BLACK)
                var blackBrush = Brushes.Black;

                // Chat bubble (bottom left corner)
                var bubblePath = "M 8,28 L 4,32 L 8,32 C 8,30.5 8,29 8,28 Z";
                drawingGroup.Children.Add(new GeometryDrawing(blackBrush, null,
                    Geometry.Parse(bubblePath)));

                // Phone inside circle
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
        /// Get list of available VoCores (ONLY VoCores, not monitors)
        /// </summary>
        public System.Collections.Generic.List<DeviceInfo> GetAvailableDevices()
        {
            var devices = new System.Collections.Generic.List<DeviceInfo>();

            try
            {
                // Use reflection to access GetAllDevices
                var getAllDevicesMethod = PluginManager.GetType().GetMethod("GetAllDevices");
                if (getAllDevicesMethod == null) return devices;

                var devicesEnumerable = getAllDevicesMethod.Invoke(PluginManager, new object[] { true }) as System.Collections.IEnumerable;
                if (devicesEnumerable == null) return devices;

                // Iterate devices
                foreach (var device in devicesEnumerable)
                {
                    var deviceType = device.GetType();

                    // FILTER: Only VoCores have Settings.UseOverlayDashboard
                    // Monitors DON'T have Information Overlay!
                    var settingsProp = deviceType.GetProperty("Settings");
                    if (settingsProp == null) continue;

                    var settings = settingsProp.GetValue(device);
                    if (settings == null) continue;

                    var settingsType = settings.GetType();
                    var overlayProp = settingsType.GetProperty("UseOverlayDashboard");

                    // If NO UseOverlayDashboard → It's a monitor, skip!
                    if (overlayProp == null) continue;

                    // It's a VoCore! Add to list
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
        /// Re-attach to VoCore and activate overlay (called when user changes device in UI)
        /// </summary>
        public void ReattachAndActivateOverlay()
        {
            // Re-attach to VoCore
            AttachToVoCore();

            // Activate overlay if attach was successful (in background to not block UI)
            if (_vocoreDevice != null && _vocoreSettings != null)
            {
                _ = Task.Run(() => EnsureOverlayActive());
            }
            else
            {
                WriteLog("❌ Could not reattach to VoCore - overlay not activated");
            }
        }

        // Class for device information
        public class DeviceInfo
        {
            public string Name { get; set; }
            public string Id { get; set; }
            public string SerialNumber { get; set; }
        }

        // ===== CONNECTION TAB PROPERTIES =====
        private string _connectionStatus = "Disconnected";
        private string _connectedNumber = "";
        private string _lastLoggedDashboard = null; // For debug - avoid log spam

        // ===== RETRY SYSTEM =====
        private bool _userRequestedDisconnect = false; // True if user clicked Disconnect
        private int _connectionRetryCount = 0;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 5000; // 5 seconds between attempts
        private bool _isRetrying = false; // Prevents multiple concurrent retries

        // ===== INTERNAL STATE (NOT EXPOSED TO SIMHUB) =====
        private List<QueuedMessage> _currentMessageGroup = null;
        private string _currentContactNumber = "";
        private string _currentContactRealNumber = "";  // Real number (e.g.: 351910203114) to send messages

        // ===== OVERLAY/DASHBOARD PROPERTIES (EXPOSED TO SIMHUB) =====
        private bool _showMessage = false; // Controls overlay visibility
        private bool _voCoreEnabled = true; // Controls VoCore display (for VR-only users)
        private string _overlaySender = "";
        private string _overlayTypeMessage = "";
        private int _overlayTotalMessages = 0;
        private string[] _overlayMessages = new string[10]; // Array of 10 messages

        /// <summary>
        /// Clear all overlay messages
        /// </summary>
        private void ClearAllOverlayMessages()
        {
            for (int i = 0; i < _overlayMessages.Length; i++)
                _overlayMessages[i] = "";
        }

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            _pluginPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimHub", "WhatsAppPlugin");

            Directory.CreateDirectory(_pluginPath);

            // Clear logs on startup (save space)
            try
            {
                var logsPath = Path.Combine(_pluginPath, "logs");
                if (Directory.Exists(logsPath))
                {
                    Directory.Delete(logsPath, true);
                }
            }
            catch { }

            // Initialize empty messages array
            for (int i = 0; i < 10; i++)
            {
                _overlayMessages[i] = "";
            }

            _settingsFile = Path.Combine(_pluginPath, "config", "settings.json");
            _contactsFile = Path.Combine(_pluginPath, "config", "contacts.json");
            _keywordsFile = Path.Combine(_pluginPath, "config", "keywords.json");

            // Ensure debug.json exists (default: disabled)
            EnsureDebugConfigExists();

            // Load settings
            LoadSettings();

            // Check if setup was already completed (node_modules exists?)
            string setupFlagPath = Path.Combine(_pluginPath, ".setup-complete");
            if (Directory.Exists(Path.Combine(_pluginPath, "node", "node_modules", "@whiskeysockets", "baileys")))
            {
                WriteLog("✅ Setup already completed previously (found .setup-complete flag)");
            }
            else
            {
                WriteLog("⚠️ First run or setup not complete (no .setup-complete flag)");
            }

            // Initialize basic components
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
            _nodeManager.InstallationCompleted += NodeManager_OnInstallationCompleted;

            // Google Contacts events
            _nodeManager.GoogleStatusReceived += NodeManager_OnGoogleStatusReceived;
            _nodeManager.GoogleAuthUrlReceived += NodeManager_OnGoogleAuthUrlReceived;
            _nodeManager.GoogleContactsDone += NodeManager_OnGoogleContactsDone;
            _nodeManager.GoogleError += NodeManager_OnGoogleError;

            // Initialize overlay renderer
            _overlayRenderer = new OverlayRenderer(_settings);

            // Install dashboard automatically
            WriteLog("=== Dashboard Installation ===");
            _dashboardInstaller = new DashboardInstaller(PluginManager, WriteLog);

            // Initialize dashboard merger
            string dashTemplatesPath = _dashboardInstaller.GetDashboardsPath();
            _dashboardMerger = new DashboardMerger(dashTemplatesPath, WriteLog);

            bool installed = _dashboardInstaller.InstallDashboard();

            if (installed)
            {
                WriteLog("✅ Dashboard installation completed successfully");
            }
            else
            {
                WriteLog("⚠️ Dashboard installation failed or dashboard already exists");
            }

            // Install overlay dashboard (for VR, etc.) if not exists
            _dashboardInstaller.InstallOverlayDashboard();

            // Check if dashboard is accessible
            bool dashExists = _dashboardInstaller.IsDashboardInstalled();
            WriteLog($"Dashboard accessible: {dashExists}");



            // IDataPlugin will call DataUpdate() automatically at 60 FPS!
            // No need for manual timer for buttons!
            WriteLog("✅ IDataPlugin enabled - button detection ready (60 FPS)");

            // Register properties in SimHub
            RegisterProperties();

            // Register actions
            RegisterActions();

            // Start setup process (check and install dependencies)
            WriteLog("=== Starting Dependency Setup ===");
            _ = InitializeDependenciesAsync();

            // Initialization log
            WriteLog("=== WhatsApp Plugin Initialized ===");
            WriteLog($"Plugin path: {_pluginPath}");
            WriteLog($"Contacts: {_settings.Contacts.Count}");
            WriteLog($"Keywords: {string.Join(", ", _settings.Keywords)}");
        }

        public void WriteLog(string message)
        {
            // Only log if debug is enabled
            if (!IsDebugLoggingEnabled()) return;

            try
            {
                var logPath = Path.Combine(_pluginPath, "logs", "plugin.log");
                var logDir = Path.GetDirectoryName(logPath);

                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch
            {
                // Ignore log errors
            }
        }

        /// <summary>
        /// Invalidate the debug logging cache so it re-reads from file
        /// </summary>
        public void InvalidateDebugLoggingCache()
        {
            _debugLoggingCache = null;
            _debugLoggingCacheTime = DateTime.MinValue;
        }

        /// <summary>
        /// Ensure debug.json exists with default value (disabled)
        /// </summary>
        private void EnsureDebugConfigExists()
        {
            try
            {
                var debugPath = Path.Combine(_pluginPath, "config", "debug.json");
                if (!File.Exists(debugPath))
                {
                    var configDir = Path.GetDirectoryName(debugPath);
                    if (!Directory.Exists(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                    }
                    // Create with debug disabled by default
                    File.WriteAllText(debugPath, "{\n  \"enabled\": false\n}");
                }
            }
            catch { }
        }

        /// <summary>
        /// Check if debug logging is enabled via config/debug.json
        /// </summary>
        private bool? _debugLoggingCache = null;
        private DateTime _debugLoggingCacheTime = DateTime.MinValue;

        private bool IsDebugLoggingEnabled()
        {
            try
            {
                // Cache for 5 seconds to avoid reading file on every log call
                if (_debugLoggingCache.HasValue && (DateTime.Now - _debugLoggingCacheTime).TotalSeconds < 5)
                {
                    return _debugLoggingCache.Value;
                }

                var debugPath = Path.Combine(_pluginPath, "config", "debug.json");
                if (File.Exists(debugPath))
                {
                    var json = File.ReadAllText(debugPath);
                    var obj = JObject.Parse(json);
                    _debugLoggingCache = obj["enabled"]?.ToObject<bool>() ?? false;
                    _debugLoggingCacheTime = DateTime.Now;
                    return _debugLoggingCache.Value;
                }

                // File doesn't exist = debug disabled
                _debugLoggingCache = false;
                _debugLoggingCacheTime = DateTime.Now;
            }
            catch
            {
                _debugLoggingCache = false;
                _debugLoggingCacheTime = DateTime.Now;
            }
            return false;
        }

        private void RegisterProperties()
        {
            // ===== CONNECTION PROPERTIES =====
            this.AttachDelegate("ConnectionStatus", () => _connectionStatus);
            this.AttachDelegate("ConnectedNumber", () => _connectedNumber);

            // ===== OVERLAY PROPERTIES (PARA DASHBOARD) =====
            // SimHub adiciona prefixo "WhatsAppPlugin." automaticamente!
            this.AttachDelegate("showmessage", () => _showMessage); // WhatsAppPlugin.showmessage
            this.AttachDelegate("vocoreenabled", () => _voCoreEnabled); // WhatsAppPlugin.vocoreenabled
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
                _messageQueue.RemoveMessagesFromContact(_currentContactRealNumber);
                WriteLog($"[QUICK REPLY {replyNumber}] 🗑️ Removed messages from {contactName} (number: {_currentContactRealNumber})");

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
            if (!string.IsNullOrEmpty(_currentContactRealNumber))
            {
                _messageQueue.RemoveMessagesFromContact(_currentContactRealNumber);
                WriteLog($"[DISMISS] Removed messages from contact {_currentContactRealNumber}");
            }
        }

        /// <summary>
        /// Limpa todas as propriedades do overlay (usado quando desconecta)
        /// </summary>
        private void ClearOverlayProperties()
        {
            _showMessage = false;
            _overlaySender = "";
            _overlayTypeMessage = "";
            _overlayTotalMessages = 0;
            ClearAllOverlayMessages();
            WriteLog("[OVERLAY] Properties cleared");
        }

        /// <summary>
        /// Mostra mensagem "No connection" no overlay
        /// </summary>
        private void ShowNoConnectionMessage()
        {
            _showMessage = true;
            _overlaySender = "No connection to WhatsApp";
            _overlayTypeMessage = "";
            _overlayTotalMessages = 1; // Manter 1 para o fundo continuar visível
            ClearAllOverlayMessages();
            WriteLog("[OVERLAY] Showing 'No connection' message");
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

            // Header - Sender (com +X se há mais mensagens na queue por mostrar)
            int showingNow = messages.Count;
            int totalInQueue = _messageQueue.GetContactMessageCount(first.Number);
            int pending = totalInQueue - showingNow;

            if (pending > 0)
                _overlaySender = $"{first.From} +{pending}";
            else
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

            // Header - Sender (com +X se há mais mensagens na queue por mostrar)
            int showingNow = groupedMessages.Count;
            int totalInQueue = _messageQueue.GetContactMessageCount(message.Number);
            int pending = totalInQueue - showingNow;

            if (pending > 0)
                _overlaySender = $"{message.From} +{pending}";
            else
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

            // Reset retry counter on successful connection
            _connectionRetryCount = 0;

            // Garantir que overlay está limpo
            _showMessage = false;
            _overlaySender = "";
            ClearAllOverlayMessages();

            // Retomar queues (caso estivessem pausadas)
            _messageQueue?.ResumeQueue();
            WriteLog("Queue resumed after successful connection");

            // 🔥 ESCONDER AVISO DE DISCONNECT
            _overlayRenderer?.Clear();

            // Request Google Contacts status (check if already connected from previous session)
            GoogleGetStatus();

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
                var isLid = messageData["isLid"]?.ToObject<bool>() ?? false;

                WriteLog($"From: {from}, Number: {number}, ChatId: {chatId}, IsLID: {isLid}, Body: {body}");

                if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(number))
                {
                    WriteLog("IGNORED: Empty body or number");
                    return;
                }

                // Normalizar número (remover +, espaços, hífens)
                var normalizedNumber = number.Replace("+", "").Replace(" ", "").Replace("-", "");

                // Se for LID, também extrair o LID do chatId para matching
                string lidNumber = null;
                if (isLid && !string.IsNullOrEmpty(chatId))
                {
                    // chatId vem como "94266210652201@lid"
                    lidNumber = chatId.Split('@')[0];
                    WriteLog($"📞 LID detected - Number: '{number}', ChatId: '{chatId}', LID: '{lidNumber}'");
                }
                else
                {
                    WriteLog($"📞 Received number: '{number}' → Normalized: '{normalizedNumber}'");
                }

                // ⭐ VERIFICAR SE É DE CONTACTO PERMITIDO!
                WriteLog($"🔍 Checking against {_settings.Contacts.Count} contacts in allowed list:");

                Contact allowedContact = null;

                foreach (var c in _settings.Contacts)
                {
                    var contactNumber = c.Number.Replace("+", "").Replace(" ", "").Replace("-", "");

                    // Tentar match normal
                    bool matchesNumber = contactNumber == normalizedNumber;

                    // Se for LID, também tentar match com o LID
                    bool matchesLid = isLid && lidNumber != null && contactNumber == lidNumber;

                    // Se for LID, também tentar match com chatId completo
                    bool matchesChatId = isLid && !string.IsNullOrEmpty(chatId) && c.Number == chatId;

                    WriteLog($"   Comparing with {c.Name}: Number:{matchesNumber} LID:{matchesLid} ChatId:{matchesChatId}");

                    if (matchesNumber || matchesLid || matchesChatId)
                    {
                        allowedContact = c;
                        WriteLog($"   ✅ MATCH found!");
                        break;
                    }
                }

                if (allowedContact == null)
                {
                    WriteLog($"❌ REJECTED: Contact '{from}' is NOT in allowed list!");
                    if (isLid && !string.IsNullOrEmpty(lidNumber))
                    {
                        WriteLog($"   This is a LID contact. Add one of these to your contacts:");
                        WriteLog($"   - LID number: {lidNumber}");
                        WriteLog($"   - Full ChatId: {chatId}");
                        WriteLog($"   - Detected number: {number}");
                    }
                    else
                    {
                        WriteLog($"   Add this number to your contacts: {number}");
                    }
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

            // Se user pediu disconnect, não tentar reconectar
            if (_userRequestedDisconnect)
            {
                WriteLog("User requested disconnect - not retrying");
                _connectionStatus = "Disconnected";
                _settingsControl?.UpdateConnectionStatus("Disconnected");
                _isRetrying = false;

                // Limpar propriedades do overlay
                ClearOverlayProperties();
                return;
            }

            // Prevent multiple concurrent retries
            if (_isRetrying)
            {
                WriteLog("Retry already in progress - ignoring duplicate error event");
                return;
            }

            // Tentar reconectar até 3 vezes
            _connectionRetryCount++;
            WriteLog($"Connection attempt {_connectionRetryCount}/{MAX_RETRY_ATTEMPTS}");

            if (_connectionRetryCount <= MAX_RETRY_ATTEMPTS)
            {
                _isRetrying = true;
                _connectionStatus = $"Reconnecting ({_connectionRetryCount}/{MAX_RETRY_ATTEMPTS})...";
                _settingsControl?.UpdateConnectionStatus($"Reconnecting ({_connectionRetryCount}/{MAX_RETRY_ATTEMPTS})...");

                // Pausar queues durante retry
                _messageQueue?.PauseQueue();
                WriteLog($"Queue paused - waiting {RETRY_DELAY_MS}ms before retry...");

                // Tentar reconectar após delay
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Wait the full delay before retrying
                        await Task.Delay(RETRY_DELAY_MS);

                        // Verificar novamente se user não pediu disconnect entretanto
                        if (_userRequestedDisconnect)
                        {
                            WriteLog("User requested disconnect during retry delay - aborting");
                            _isRetrying = false;
                            return;
                        }

                        WriteLog($"Retry attempt {_connectionRetryCount} starting now...");
                        _nodeManager?.Stop();
                        await Task.Delay(1000); // Wait for process to fully stop

                        await _nodeManager.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Retry {_connectionRetryCount} failed: {ex.Message}");
                        // O próximo erro vai disparar NodeManager_OnError novamente
                    }
                    finally
                    {
                        _isRetrying = false;
                    }
                });
            }
            else
            {
                // Falhou todas as tentativas
                WriteLog($"All {MAX_RETRY_ATTEMPTS} reconnection attempts failed");
                _connectionStatus = "Connection Failed";
                _settingsControl?.UpdateConnectionStatus("Connection Failed");
                _isRetrying = false;

                // Pausar queues
                _messageQueue?.PauseQueue();

                // Mostrar mensagem fixa no ecrã
                ShowNoConnectionMessage();
            }
        }

        private void NodeManager_OnStatusChanged(object sender, string status)
        {
            WriteLog($"📡 Status changed: {status}");

            if (status == "Installing")
            {
                _connectionStatus = "Installing dependencies...";
                _settingsControl?.UpdateConnectionStatus("Installing dependencies...");

                // Desabilitar botões durante instalação
                _settingsControl?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    if (_settingsControl?.ReconnectButton != null)
                    {
                        _settingsControl.ReconnectButton.IsEnabled = false;
                        _settingsControl.ReconnectButton.ToolTip = "Installing dependencies...";
                    }
                    if (_settingsControl?.DisconnectButton != null)
                    {
                        _settingsControl.DisconnectButton.IsEnabled = false;
                    }
                }));
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

                // Se script foi atualizado, refrescar versão na UI
                if (status.Contains("Updated script") || status.Contains("Created script"))
                {
                    _settingsControl?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        _settingsControl?.RefreshScriptsVersion();
                    }));
                }
            }
        }

        private void NodeManager_OnInstallationCompleted(object sender, bool success)
        {
            WriteLog($"📦 Installation completed: {(success ? "SUCCESS" : "FAILED")}");

            // Atualizar UI na thread correta
            _settingsControl?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (success)
                {
                    _settingsControl?.UpdateConnectionStatus("Connecting");
                    // Botões serão re-habilitados quando Connected/Ready
                }
                else
                {
                    _settingsControl?.UpdateConnectionStatus("Installation failed");
                    // Re-habilitar botões para permitir retry
                    if (_settingsControl?.ReconnectButton != null)
                    {
                        _settingsControl.ReconnectButton.IsEnabled = true;
                        _settingsControl.ReconnectButton.ToolTip = null;
                    }
                }
            }));
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

        #region Google Contacts Event Handlers

        private void NodeManager_OnGoogleStatusReceived(object sender, (bool connected, string status) args)
        {
            WriteLog($"📇 Google status: connected={args.connected}, status={args.status}");
            _settingsControl?.UpdateGoogleStatus(args.status, args.connected);

            // Load contacts from file when Google is connected
            if (args.connected)
            {
                WriteLog("📇 Google connected - loading contacts from file...");
                LoadGoogleContactsFromFile();
            }
        }

        private void NodeManager_OnGoogleAuthUrlReceived(object sender, string url)
        {
            WriteLog($"📇 Google auth URL received: {(url != null ? url.Substring(0, System.Math.Min(80, url.Length)) : "NULL")}...");

            if (string.IsNullOrEmpty(url))
            {
                WriteLog("❌ Google auth URL is empty!");
                _settingsControl?.HandleGoogleError("Authentication URL is empty");
                return;
            }

            // Open browser for authentication
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                WriteLog("📇 Browser opened for Google authentication");
                _settingsControl?.UpdateGoogleStatus("Waiting for auth in browser...", false);
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Failed to open browser: {ex.Message}");
                _settingsControl?.HandleGoogleError($"Failed to open browser: {ex.Message}");
            }
        }

        private void NodeManager_OnGoogleContactsDone(object sender, EventArgs e)
        {
            WriteLog("📇 Google contacts refresh done - reading from file...");
            LoadGoogleContactsFromFile();
        }

        /// <summary>
        /// Load Google contacts directly from the JSON file
        /// </summary>
        private void LoadGoogleContactsFromFile()
        {
            try
            {
                var contactsFile = Path.Combine(_pluginPath, "data_google", "contacts.json");

                if (!File.Exists(contactsFile))
                {
                    WriteLog("📇 No Google contacts file found - fetching from API...");
                    GoogleGetContacts(true); // Go to API when no local file exists
                    return;
                }

                var json = File.ReadAllText(contactsFile);
                var data = JObject.Parse(json);
                var contactsArray = data["contacts"] as JArray;

                var contactsList = new System.Collections.ObjectModel.ObservableCollection<Contact>();

                if (contactsArray != null)
                {
                    foreach (var contact in contactsArray)
                    {
                        var name = contact["name"]?.ToString();
                        var number = contact["number"]?.ToString();

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(number))
                        {
                            contactsList.Add(new Contact
                            {
                                Name = name,
                                Number = number,
                                IsVip = false
                            });
                        }
                    }
                }

                WriteLog($"📇 Loaded {contactsList.Count} Google contacts from file");
                _settingsControl?.UpdateGoogleContactsList(contactsList);
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error reading Google contacts file: {ex.Message}");
            }
        }

        private void NodeManager_OnGoogleError(object sender, string error)
        {
            WriteLog($"❌ Google error: {error}");
            _settingsControl?.HandleGoogleError(error);
        }

        #endregion

        #region Google Contacts Methods

        public async void GoogleGetStatus()
        {
            WriteLog("📇 Checking Google status...");
            try
            {
                // First check locally if tokens file exists
                var tokensFile = Path.Combine(_pluginPath, "data_google", "tokens.json");
                if (File.Exists(tokensFile))
                {
                    WriteLog("📇 Google tokens file found - checking with backend...");
                }

                // Ask backend to verify token validity
                if (_nodeManager != null)
                {
                    await _nodeManager.SendCommandAsync("googleGetStatus");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error getting Google status: {ex.Message}");
            }
        }

        public async void GoogleStartAuth()
        {
            WriteLog("📇 Starting Google authentication...");
            try
            {
                if (_nodeManager != null)
                {
                    await _nodeManager.SendCommandAsync("googleStartAuth");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error starting Google auth: {ex.Message}");
                _settingsControl?.HandleGoogleError(ex.Message);
            }
        }

        public async void GoogleGetContacts(bool forceRefresh = true)
        {
            WriteLog($"📇 Requesting Google contacts (forceRefresh={forceRefresh})...");
            try
            {
                if (_nodeManager != null)
                {
                    // Send command with forceRefresh parameter to fetch from API instead of cache
                    var command = new { type = "googleGetContacts", forceRefresh = forceRefresh };
                    await _nodeManager.SendJsonAsync(command);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error getting Google contacts: {ex.Message}");
                _settingsControl?.HandleGoogleError(ex.Message);
            }
        }

        public async void GoogleDisconnect()
        {
            WriteLog("📇 Disconnecting from Google...");
            try
            {
                if (_nodeManager != null)
                {
                    await _nodeManager.SendCommandAsync("googleDisconnect");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error disconnecting from Google: {ex.Message}");
            }
        }

        #endregion

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

            WriteLog($"[EVENT] ✅ OnMessageRemoved completed - overlay cleared, queue count = {_messageQueue.GetQueueSize()}");
        }

        public void End(PluginManager pluginManager)
        {
            WriteLog("=== WhatsApp Plugin Shutting Down ===");

            SaveSettings();

            // Clean disconnect - same as clicking Disconnect button
            // This sends shutdown command to Node.js and waits for clean exit
            if (_nodeManager != null)
            {
                WriteLog("Disconnecting Node.js...");
                _nodeManager.Stop();
                _nodeManager.Dispose();
            }

            _messageQueue?.Dispose();

            WriteLog("Plugin shutdown complete");
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

                // Sync VoCore enabled property with settings
                _voCoreEnabled = _settings.VoCoreEnabled;
            }
            catch (Exception)
            {
                // ⚠️ Erro ao ler - criar novas
                _settings = new PluginSettings();
                _settings.EnsureDefaults();
                SaveSettings();
                _voCoreEnabled = _settings.VoCoreEnabled;
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
            // Always show SettingsControl - dependencies shown in Connection tab
            if (_settingsControl == null)
            {
                _settingsControl = new UI.SettingsControl(this);
            }
            return _settingsControl;
        }

        // Métodos públicos para a UI
        public void DisconnectWhatsApp()
        {
            // Log stack trace to find who called this
            var stackTrace = new System.Diagnostics.StackTrace(true);
            WriteLog($"User requested disconnect - Called from:\n{stackTrace}");

            // Marcar que foi o user que pediu disconnect (não tentar reconectar)
            _userRequestedDisconnect = true;
            _connectionRetryCount = 0;
            _isRetrying = false;

            // Limpar ambas as queues
            _messageQueue?.ClearQueue();
            WriteLog("Queues cleared on user disconnect");

            // Limpar overlay
            _showMessage = false;
            _overlaySender = "";
            ClearAllOverlayMessages();

            _nodeManager?.Stop();
        }

        public async System.Threading.Tasks.Task ReconnectWhatsApp()
        {
            WriteLog("User requested connect...");

            // Reset flags - user quer conectar
            _userRequestedDisconnect = false;
            _connectionRetryCount = 0;
            _isRetrying = false;

            // Garantir que overlay está limpo
            _showMessage = false;
            _overlaySender = "";
            ClearAllOverlayMessages();

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

        /// <summary>
        /// Muda o backend (whatsapp-web.js <-> Baileys) e faz reconnect automático
        /// </summary>
        public async System.Threading.Tasks.Task SwitchBackend(string newBackend)
        {
            WriteLog($"🔄 Switching backend to: {newBackend}");

            try
            {
                // 1. Parar o nodeManager atual completamente
                WriteLog("Stopping current backend...");
                _nodeManager?.Stop();

                // 2. Aguardar para garantir que Node.js/Chrome terminaram
                await System.Threading.Tasks.Task.Delay(2000);

                // 3. Desregistar eventos do nodeManager antigo
                if (_nodeManager != null)
                {
                    _nodeManager.OnQrCode -= NodeManager_OnQrCode;
                    _nodeManager.OnReady -= NodeManager_OnReady;
                    _nodeManager.OnMessage -= NodeManager_OnMessage;
                    _nodeManager.OnError -= NodeManager_OnError;
                    _nodeManager.StatusChanged -= NodeManager_OnStatusChanged;
                    _nodeManager.ChatContactsListReceived -= NodeManager_OnChatContactsListReceived;
                    _nodeManager.ChatContactsError -= NodeManager_OnChatContactsError;
                    _nodeManager.InstallationCompleted -= NodeManager_OnInstallationCompleted;
                    _nodeManager.GoogleStatusReceived -= NodeManager_OnGoogleStatusReceived;
                    _nodeManager.GoogleAuthUrlReceived -= NodeManager_OnGoogleAuthUrlReceived;
                    _nodeManager.GoogleContactsDone -= NodeManager_OnGoogleContactsDone;
                    _nodeManager.GoogleError -= NodeManager_OnGoogleError;
                }

                // 4. Criar novo nodeManager com o backend escolhido
                WriteLog($"Creating new WebSocketManager with backend: {newBackend}");
                _nodeManager = new WebSocketManager(_pluginPath, newBackend);
                _nodeManager.OnQrCode += NodeManager_OnQrCode;
                _nodeManager.OnReady += NodeManager_OnReady;
                _nodeManager.OnMessage += NodeManager_OnMessage;
                _nodeManager.OnError += NodeManager_OnError;
                _nodeManager.StatusChanged += NodeManager_OnStatusChanged;
                _nodeManager.ChatContactsListReceived += NodeManager_OnChatContactsListReceived;
                _nodeManager.ChatContactsError += NodeManager_OnChatContactsError;
                _nodeManager.InstallationCompleted += NodeManager_OnInstallationCompleted;
                _nodeManager.GoogleStatusReceived += NodeManager_OnGoogleStatusReceived;
                _nodeManager.GoogleAuthUrlReceived += NodeManager_OnGoogleAuthUrlReceived;
                _nodeManager.GoogleContactsDone += NodeManager_OnGoogleContactsDone;
                _nodeManager.GoogleError += NodeManager_OnGoogleError;

                // 5. Iniciar o novo backend
                WriteLog($"Starting {newBackend} backend...");
                await _nodeManager.StartAsync();

                WriteLog($"✅ Backend switched to {newBackend} successfully!");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ Error switching backend: {ex.Message}");
                throw;
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
        /// Update VoCore enabled state (called from UI)
        /// This syncs the property exposed to dashboards
        /// </summary>
        public void SetVoCoreEnabled(bool enabled)
        {
            _voCoreEnabled = enabled;
            _settings.VoCoreEnabled = enabled;
            WriteLog($"VoCore enabled: {enabled}");
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
        /// THROTTLE: Só verifica dashboard (I/O) a cada 10 segundos
        /// </summary>
        private void EnsureOverlayActive()
        {
            if (_vocoreSettings == null)
            {
                return;
            }

            try
            {
                // 🔥 THROTTLE: Verificar dashboard (I/O) apenas 1x a cada 10 segundos
                var timeSinceLastCheck = (DateTime.Now - _lastDashboardCheck).TotalSeconds;
                if (timeSinceLastCheck >= 10)
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
                if (useOverlayProp == null)
                {
                    return; // Propriedade não existe, sair
                }

                var isActive = (bool)useOverlayProp.GetValue(_vocoreSettings);

                if (!isActive)
                {
                    // Ligar overlay
                    useOverlayProp.SetValue(_vocoreSettings, true);
                    WriteLog("✅ Information overlay activated");
                }
                // Se já está ligado, não faz nada

                // PASSO 2: Verificar e configurar dashboard (com merge se necessário)
                var overlayDashboardProp = settingsType.GetProperty("CurrentOverlayDashboard");
                if (overlayDashboardProp != null)
                {
                    var overlayDashboard = overlayDashboardProp.GetValue(_vocoreSettings);
                    if (overlayDashboard != null)
                    {

                        // Obter dashboard atual usando propriedade Dashboard
                        string currentDashboard = null;
                        var dashProp = overlayDashboard.GetType().GetProperty("Dashboard");
                        if (dashProp != null)
                        {
                            currentDashboard = dashProp.GetValue(overlayDashboard) as string;
                        }

                        // 🔍 DEBUG: Log do valor exacto retornado pelo SimHub (apenas quando muda)
                        if (currentDashboard != _lastLoggedDashboard)
                        {
                            WriteLog($"🔍 SimHub CurrentOverlayDashboard = \"{currentDashboard ?? "null"}\"");
                            _lastLoggedDashboard = currentDashboard;
                        }

                        // 🔥 LÓGICA DE DASHBOARD - toda a verificação está em DetermineDashboardToSet
                        // (verifica existência, decide se é nosso/merged/outro, faz merge se preciso)
                        string targetDashboard = DetermineDashboardToSet(currentDashboard);

                        // Se precisa mudar dashboard
                        if (targetDashboard != null && targetDashboard != currentDashboard)
                        {
                            var trySetMethod = overlayDashboard.GetType().GetMethod("TrySet");
                            if (trySetMethod != null)
                            {
                                WriteLog($"📊 Changing dashboard: {currentDashboard ?? "none"} → {targetDashboard}");
                                trySetMethod.Invoke(overlayDashboard, new object[] { targetDashboard });

                                // IMPORTANTE: TrySet pode desligar o overlay - garantir que fica ligado
                                var isStillActive = (bool)useOverlayProp.GetValue(_vocoreSettings);
                                if (!isStillActive)
                                {
                                    useOverlayProp.SetValue(_vocoreSettings, true);
                                    WriteLog("✅ Re-activated overlay after dashboard change");
                                }
                            }
                        }
                    }
                }
                // ✅ Tudo OK - não faz log "Overlay already configured" (silencioso)
            }
            catch (Exception ex)
            {
                WriteLog($"⚠️ EnsureOverlayActive error: {ex.Message}");
            }
        }

        /// <summary>
        /// Determina qual dashboard deve ser configurado no Information Overlay
        /// Lógica (ORDEM IMPORTANTE):
        /// 1. Nenhum dashboard definido → WhatsAppPlugin (não verifica mais nada)
        /// 2. Dashboard definido mas não existe → WhatsAppPlugin
        /// 3. Dashboard é WhatsAppPlugin → null (não muda)
        /// 4. Dashboard é merged → null (não muda)
        /// 5. Dashboard é outro (existe) → fazer merge
        /// </summary>
        private string DetermineDashboardToSet(string currentDashboard)
        {
            try
            {
                const string OUR_DASHBOARD = "WhatsAppPlugin";
                string MERGED_DASHBOARD = DashboardMerger.MergedDashboardName;

                // PASSO 1: Nenhum dashboard definido → instalar o nosso e SAIR
                if (string.IsNullOrEmpty(currentDashboard))
                {
                    WriteLog("📋 No dashboard in Information Overlay → Setting WhatsAppPlugin");
                    return OUR_DASHBOARD;
                }

                // PASSO 2: Verificar se currentDashboard EXISTE no disco ANTES de qualquer comparação
                // (SimHub pode ter referência a dashboard apagado)
                bool dashExists = _dashboardMerger.DashboardExists(currentDashboard);
                if (!dashExists)
                {
                    WriteLog($"⚠️ Dashboard '{currentDashboard}' is defined but does not exist on disk");
                    WriteLog($"→ Setting WhatsAppPlugin");
                    return OUR_DASHBOARD;
                }

                // PASSO 3: É o nosso dashboard → não mexer (case-insensitive)
                if (string.Equals(currentDashboard, OUR_DASHBOARD, StringComparison.OrdinalIgnoreCase))
                {
                    // Silencioso - já está OK
                    return null;
                }

                // PASSO 4: É o merged dashboard → não mexer (case-insensitive)
                // Também verificar se COMEÇA com o nome do merged (SimHub pode adicionar sufixo)
                if (string.Equals(currentDashboard, MERGED_DASHBOARD, StringComparison.OrdinalIgnoreCase) ||
                    currentDashboard.StartsWith(MERGED_DASHBOARD, StringComparison.OrdinalIgnoreCase))
                {
                    // Silencioso - já está OK
                    return null;
                }

                // PASSO 5: É outro dashboard (e existe, já confirmámos no PASSO 2) → fazer merge
                WriteLog($"🔀 Found different dashboard: {currentDashboard} → Merging with WhatsAppPlugin");
                string mergedDashboard = _dashboardMerger.MergeDashboards(currentDashboard, OUR_DASHBOARD);

                if (mergedDashboard != null)
                {
                    WriteLog($"✅ Merge successful → {mergedDashboard}");
                    return mergedDashboard;
                }
                else
                {
                    WriteLog("❌ Merge failed → Falling back to WhatsAppPlugin only");
                    return OUR_DASHBOARD;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"⚠️ DetermineDashboardToSet error: {ex.Message}");
                return "WhatsAppPlugin"; // Fallback
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
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100).ConfigureAwait(false);
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
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100).ConfigureAwait(false);
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
                while (_settingsControl == null && retries < 30)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    retries++;
                }

                if (_settingsControl != null)
                {
                    WriteLog("✅ Setup UI ready! Initializing status...");

                    // DESACTIVAR BOTÕES CONNECTION DURANTE INSTALAÇÃO
                    _settingsControl.Dispatcher.Invoke(() =>
                    {
                        _settingsControl.DisconnectButton.IsEnabled = false;
                        _settingsControl.DisconnectButton.ToolTip = "Installing dependencies...";
                        _settingsControl.ReconnectButton.IsEnabled = false;
                        _settingsControl.ReconnectButton.ToolTip = "Installing dependencies...";
                    });

                    // INICIALIZAR TODOS OS STATUS EXPLICITAMENTE
                    _settingsControl.UpdateNodeStatus("Checking...", false);
                    _settingsControl.UpdateGitStatus("Waiting...", false);
                    _settingsControl.UpdateNpmStatus("Waiting...", false);
                    // _settingsControl.UpdateProgress(0, "Checking dependencies...");

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

                bool nodeWasInstalled = false;

                if (!nodeInstalled)
                {
                    WriteLog("⚠️ Node.js not found - installing automatically...");

                    if (_settingsControl != null)
                    {
                        _settingsControl.UpdateNodeStatus("Installing Node.js portable...", false);
                        // _settingsControl.UpdateProgress(10, "Installing Node.js...");
                    }

                    bool success = await _dependencyManager.InstallNodeSilently().ConfigureAwait(false);

                    if (success)
                    {
                        WriteLog("✅ Node.js portable installed!");
                        nodeWasInstalled = true;

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
                                if (_settingsControl != null)
                                {
                                    _settingsControl.UpdateNodeStatus($"Installed ({nodeVersion})", true);
                                    // _settingsControl.UpdateProgress(33, "Node.js ready!");
                                }
                            }
                            else
                            {
                                WriteLog("⚠️ WARNING: Node.js installed but cannot execute - may need PATH refresh");
                                if (_settingsControl != null)
                                {
                                    _settingsControl.UpdateNodeStatus("Installed (PATH pending)", true);
                                    // _settingsControl.UpdateProgress(33, "Node.js installed!");
                                }
                            }
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Node.js installed but verification failed");
                            if (_settingsControl != null)
                            {
                                _settingsControl.UpdateNodeStatus("Installed (verification pending)", true);
                                // _settingsControl.UpdateProgress(33, "Node.js installed!");
                            }
                        }
                    }
                    else
                    {
                        WriteLog("❌ ERROR: Failed to install Node.js!");
                        if (_settingsControl != null)
                        {
                            _settingsControl.UpdateNodeStatus("Installation failed", false, true);
                            // _settingsControl.UpdateProgress(0, "ERROR: Node.js installation failed");
                            // _settingsControl.ShowRetryButton(); // MOSTRAR BOTÃO RETRY!
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

                    if (_settingsControl != null)
                    {
                        if (canExecute && !string.IsNullOrEmpty(nodeVersion))
                        {
                            WriteLog($"Updating UI: Node.js status = Installed ({nodeVersion})");
                            _settingsControl.UpdateNodeStatus($"Installed ({nodeVersion})", true);
                            // _settingsControl.UpdateProgress(33, "Node.js ready!");
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Node.js found but cannot execute!");
                            _settingsControl.UpdateNodeStatus("Found (cannot execute)", true);
                            // _settingsControl.UpdateProgress(33, "Node.js found!");
                        }
                        WriteLog("UI updated successfully!");

                        // Delay para garantir que UI renderiza
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                    else
                    {
                        WriteLog("❌ ERROR: _settingsControl is NULL! Cannot update UI!");
                    }
                }

                WriteLog("Node.js check complete! Moving to Git...");

                // ============ GIT ============
                WriteLog("🔍 Checking Git...");

                bool gitInstalled = _dependencyManager.IsGitInstalled();
                bool gitWasInstalled = false;

                if (!gitInstalled)
                {
                    WriteLog("⚠️ Git not found - installing automatically...");

                    if (_settingsControl != null)
                    {
                        _settingsControl.UpdateGitStatus("Installing Git...", false);
                        // _settingsControl.UpdateProgress(40, "Installing Git...");
                    }

                    bool success = await _dependencyManager.InstallGitSilently().ConfigureAwait(false);

                    if (success)
                    {
                        WriteLog("✅ Git portable installed!");
                        gitWasInstalled = true;

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
                                if (_settingsControl != null)
                                {
                                    _settingsControl.UpdateGitStatus($"Installed ({gitVersion})", true);
                                    // _settingsControl.UpdateProgress(66, "Git ready!");
                                }
                            }
                            else
                            {
                                WriteLog("⚠️ WARNING: Git installed but cannot execute - may need PATH refresh");
                                if (_settingsControl != null)
                                {
                                    _settingsControl.UpdateGitStatus("Installed (PATH pending)", true);
                                    // _settingsControl.UpdateProgress(66, "Git installed!");
                                }
                            }
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Git installed but verification failed");
                            if (_settingsControl != null)
                            {
                                _settingsControl.UpdateGitStatus("Installed (verification pending)", true);
                                // _settingsControl.UpdateProgress(66, "Git installed!");
                            }
                        }
                    }
                    else
                    {
                        WriteLog("❌ ERROR: Failed to install Git!");
                        if (_settingsControl != null)
                        {
                            _settingsControl.UpdateGitStatus("Installation failed", false, true);
                            // _settingsControl.UpdateProgress(0, "ERROR: Git installation failed");
                            // _settingsControl.ShowRetryButton(); // MOSTRAR BOTÃO RETRY!
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

                    if (_settingsControl != null)
                    {
                        if (canExecute && !string.IsNullOrEmpty(gitVersion))
                        {
                            WriteLog($"Updating UI: Git status = Installed ({gitVersion})");
                            _settingsControl.UpdateGitStatus($"Installed ({gitVersion})", true);
                            // _settingsControl.UpdateProgress(66, "Git ready!");
                        }
                        else
                        {
                            WriteLog("⚠️ WARNING: Git found but cannot execute!");
                            _settingsControl.UpdateGitStatus("Found (cannot execute)", true);
                            // _settingsControl.UpdateProgress(66, "Git found!");
                        }
                    }
                }

                // ============ NPM PACKAGES ============
                WriteLog("🔍 Checking npm packages...");

                // SE instalou Node OU Git, apagar node_modules por segurança
                if (nodeWasInstalled || gitWasInstalled)
                {
                    WriteLog("⚠️ Node or Git was just installed - deleting node_modules for safety...");
                    string nodeModulesPath = Path.Combine(_pluginPath, "node", "node_modules");
                    if (Directory.Exists(nodeModulesPath))
                    {
                        try
                        {
                            Directory.Delete(nodeModulesPath, true);
                            WriteLog("✅ node_modules deleted successfully");
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"⚠️ Could not delete node_modules: {ex.Message}");
                        }
                    }
                }

                bool packagesInstalled = _dependencyManager.AreNpmPackagesInstalled();

                if (!packagesInstalled)
                {
                    WriteLog("⚠️ npm packages not found - installing...");

                    if (_settingsControl != null)
                    {
                        _settingsControl.Dispatcher.Invoke(() =>
                        {
                            _settingsControl.SetDependenciesInstalling(true, "Installing npm packages...");
                            _settingsControl.UpdateNpmStatus("Installing...", false);
                        });
                    }

                    bool success = await _dependencyManager.InstallNpmPackages().ConfigureAwait(false);

                    if (success)
                    {
                        WriteLog("✅ npm packages installed successfully!");

                        // Verificar individualmente cada biblioteca
                        bool whatsappWebJsInstalled = _dependencyManager.IsWhatsAppWebJsInstalled();
                        bool baileysInstalled = _dependencyManager.IsBaileysInstalled();

                        if (_settingsControl != null)
                        {
                            _settingsControl.Dispatcher.Invoke(() =>
                            {
                                _settingsControl.UpdateNpmStatus("Installed", true);
                                _settingsControl.SetDependenciesInstalling(false);
                            });

                            if (whatsappWebJsInstalled)
                            {
                                WriteLog("✅ whatsapp-web.js library verified");
                            }
                            else
                            {
                                WriteLog("⚠️ whatsapp-web.js library not found after installation");
                            }

                            if (baileysInstalled)
                            {
                                WriteLog("✅ Baileys library verified");
                            }
                            else
                            {
                                WriteLog("⚠️ Baileys library not found after installation");
                            }
                        }
                    }
                    else
                    {
                        WriteLog("❌ ERROR: Failed to install npm packages!");
                        if (_settingsControl != null)
                        {
                            _settingsControl.Dispatcher.Invoke(() =>
                            {
                                _settingsControl.UpdateNpmStatus("Installation failed", false, true);
                                _settingsControl.SetDependenciesInstalling(false);
                            });
                            // _settingsControl.UpdateProgress(0, "ERROR: npm install failed");
                            // _settingsControl.ShowRetryButton();
                        }
                        return;
                    }
                }
                else
                {
                    WriteLog("✅ npm packages already installed - verifying libraries...");

                    // Actualizar UI para mostrar que já estão instalados
                    if (_settingsControl != null)
                    {
                        _settingsControl.Dispatcher.Invoke(() =>
                        {
                            _settingsControl.UpdateNpmStatus("Installed", true);
                        });
                    }

                    // Verificar individualmente cada biblioteca
                    bool whatsappWebJsInstalled = _dependencyManager.IsWhatsAppWebJsInstalled();
                    bool baileysInstalled = _dependencyManager.IsBaileysInstalled();

                    if (_settingsControl != null)
                    {
                        if (whatsappWebJsInstalled)
                        {
                            // _settingsControl.UpdateWhatsAppWebJsStatus("Already installed", true);
                            WriteLog("✅ whatsapp-web.js library found");
                        }
                        else
                        {
                            // _settingsControl.UpdateWhatsAppWebJsStatus("Not found", false, true);
                            WriteLog("⚠️ whatsapp-web.js library not found");
                        }

                        if (baileysInstalled)
                        {
                            // _settingsControl.UpdateBaileysStatus("Already installed", true);
                            WriteLog("✅ Baileys library found");
                        }
                        else
                        {
                            // _settingsControl.UpdateBaileysStatus("Not found", false, true);
                            WriteLog("⚠️ Baileys library not found");
                        }

                        if (whatsappWebJsInstalled && baileysInstalled)
                        {
                            // _settingsControl.UpdateProgress(100, "All dependencies ready!");
                        }
                        else
                        {
                            // _settingsControl.UpdateProgress(90, "Some libraries missing - plugin may not work correctly");
                        }
                    }
                }


                // ============ TUDO PRONTO! ============
                WriteLog("✅ All dependencies installed - starting Node.js...");

                // REACTIVAR BOTÕES CONNECTION
                if (_settingsControl != null)
                {
                    _settingsControl.Dispatcher.Invoke(() =>
                    {
                        _settingsControl.DisconnectButton.IsEnabled = false; // Disconnected state
                        _settingsControl.DisconnectButton.ToolTip = null;
                        _settingsControl.ReconnectButton.IsEnabled = true;   // Allow reconnect
                        _settingsControl.ReconnectButton.ToolTip = null;
                    });
                    WriteLog("✅ Connection buttons re-enabled");
                }

                // Mostrar botão Continue!
                if (_settingsControl != null)
                {
                    // _settingsControl.ShowContinueButton();
                }

                // Aguardar 1s para user ver a UI completa
                await Task.Delay(1000).ConfigureAwait(false);

                // Agora sim, arrancar Node.js!
                await StartNodeJs().ConfigureAwait(false);

                // Tentar anexar ao VoCore se já configurado
                if (!string.IsNullOrEmpty(_settings.TargetDevice))
                {
                    AttachToVoCore();

                    // Auto-ativar overlay (em background para não bloquear)
                    if (_vocoreDevice != null)
                    {
                        WriteLog("🎯 Auto-activating overlay...");
                        await Task.Delay(1000).ConfigureAwait(false);
                        _ = Task.Run(() => EnsureOverlayActive());
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

        /// <summary>
        /// Event handler quando user clica no botão Restart SimHub
        /// </summary>


        #endregion
    }
}
