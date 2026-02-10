using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using SimHub.Plugins;
using SimHub.Plugins.OutputPlugins.GraphicalDash;
using SimHub.Plugins.OutputPlugins.GraphicalDash.BitmapDisplay;
using SimHub.Plugins.Devices;
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
        // Plugin version - update this when releasing new versions
        public const string PLUGIN_VERSION = "1.0.7";
        private const string GITHUB_REPO = "BrunoSilva1978PT/WhatsApp-SimHub-Plugin";

        public PluginManager PluginManager { get; set; }
        public ImageSource PictureIcon => CreateWhatsAppIcon();
        public string LeftMenuTitle => "WhatsApp Plugin";

        private PluginSettings _settings;
        private WebSocketManager _nodeManager;
        private MessageQueue _messageQueue;

        private bool _isTestingMessage = false; // Flag to block queues during test

        private DashboardInstaller _dashboardInstaller; // Installer to reinstall dashboard
        private DashboardMerger _dashboardMerger; // Merger to combine dashboards
        private DeviceDiscoveryManager _deviceDiscovery; // Unified device discovery (VoCore + LED)
        private VoCoreManager _vocoreManager; // VoCore configuration manager
        private SoundManager _soundManager; // Sound notification manager
        private LedEffectsManager _ledManager; // LED notification effects manager

        // QUICK REPLIES: Now work via registered Actions
        // See RegisterActions() and SendQuickReply(int)
        private bool _replySentForCurrentMessage = false; // Blocks multiple sends for same message

        // SOUND NOTIFICATIONS: Track last contact that played sound to avoid repeating
        private string _lastSoundPlayedForContact = ""; // Contact number that last played sound

        // LED NOTIFICATIONS: Track last contact that triggered LED to avoid repeating
        private string _lastLedPlayedForContact = ""; // Contact number that last triggered LED

        private string _pluginPath;
        private string _settingsFile;
        private string _contactsFile;
        private string _keywordsFile;
        private UI.SettingsControl _settingsControl;

        // SETUP & DEPENDENCIES
        private DependencyManager _dependencyManager;
        public DependencyManager DependencyManager => _dependencyManager;
        // SetupControl removed - dependencies now managed in SettingsControl Connection tab

        // Public property for settings access
        public PluginSettings Settings => _settings;

        // Property to check if Node.js script is running
        public bool IsScriptRunning => _nodeManager?.IsConnected ?? false;

        // Property to check full connection status (Node.js + WebSocket + WhatsApp)
        public bool IsFullyConnected => IsScriptRunning && _isWhatsAppConnected;

        // Property to check WhatsApp connection status
        public bool IsWhatsAppConnected => _isWhatsAppConnected;

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
        /// Get list of available VoCores (uses VoCoreManager)
        /// </summary>
        public List<VoCoreDevice> GetAvailableDevices()
        {
            return _vocoreManager?.GetConnectedDevices() ?? new List<VoCoreDevice>();
        }

        // ===== CONNECTION TAB PROPERTIES =====
        private string _connectionStatus = "Disconnected";
        private string _connectedNumber = "";

        // ===== RECONNECTION SYSTEM =====
        private bool _userRequestedDisconnect = false; // True when user clicks Disconnect button
        private int _reconnectAttempts = 0;
        private const int MAX_RECONNECT_ATTEMPTS = 5;
        private const int RECONNECT_INTERVAL_MS = 15000; // 15 seconds between attempts
        private System.Timers.Timer _reconnectTimer;
        private bool _isWhatsAppConnected = false; // True when backend confirms WhatsApp connection
        private bool _isWaitingForQrCode = false; // True when waiting for user to scan QR code

        // ===== DATAUPDATE VERIFICATION (Every 3 seconds) =====
        private DateTime _lastDataUpdateCheck = DateTime.MinValue;
        private const int DATA_UPDATE_INTERVAL_MS = 5000; // 5 seconds

        // ===== INTERNAL STATE (NOT EXPOSED TO SIMHUB) =====
        private List<QueuedMessage> _currentMessageGroup = null;
        private string _currentContactNumber = "";
        private string _currentContactRealNumber = "";  // Real number (e.g.: 351910203114) to send messages

        // ===== OVERLAY/DASHBOARD PROPERTIES (EXPOSED TO SIMHUB) =====
        private bool _showMessage = false; // Controls overlay visibility
        private bool _voCoreEnabled = true; // Legacy: Controls VoCore display (= vocore1enabled || vocore2enabled)
        private bool _voCore1Enabled = false; // Controls VoCore 1 display
        private bool _voCore2Enabled = false; // Controls VoCore 2 display
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
            // Google Contacts events
            _nodeManager.GoogleStatusReceived += NodeManager_OnGoogleStatusReceived;
            _nodeManager.GoogleAuthUrlReceived += NodeManager_OnGoogleAuthUrlReceived;
            _nodeManager.GoogleContactsDone += NodeManager_OnGoogleContactsDone;
            _nodeManager.GoogleError += NodeManager_OnGoogleError;

            // WhatsApp number verification
            _nodeManager.CheckWhatsAppResult += NodeManager_OnCheckWhatsAppResult;

            // Initialize overlay renderer


            // Install dashboard automatically
            WriteLog("=== Dashboard Installation ===");
            _dashboardInstaller = new DashboardInstaller(WriteLog);

            // Initialize dashboard merger
            string dashTemplatesPath = _dashboardInstaller.GetDashboardsPath();
            _dashboardMerger = new DashboardMerger(dashTemplatesPath, WriteLog);

            // Initialize unified device discovery (shared by VoCore and LED managers)
            _deviceDiscovery = new DeviceDiscoveryManager(PluginManager, WriteLog);

            // Initialize VoCore manager
            _vocoreManager = new VoCoreManager(PluginManager, _dashboardMerger, _deviceDiscovery, WriteLog);

            // Initialize sound manager
            _soundManager = new SoundManager(_pluginPath, WriteLog);
            _soundManager.ExtractDefaultSounds();

            // Initialize LED effects manager
            _ledManager = new LedEffectsManager(PluginManager, _deviceDiscovery, WriteLog);

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

            // Apply saved VoCore dashboards on startup
            if (!string.IsNullOrEmpty(_settings?.VoCore1_Serial))
                ApplyDashboardFromSettings(1);
            if (!string.IsNullOrEmpty(_settings?.VoCore2_Serial))
                ApplyDashboardFromSettings(2);

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
            this.AttachDelegate("vocoreenabled", () => _voCoreEnabled); // WhatsAppPlugin.vocoreenabled (legacy)
            this.AttachDelegate("vocore1enabled", () => _voCore1Enabled); // WhatsAppPlugin.vocore1enabled
            this.AttachDelegate("vocore2enabled", () => _voCore2Enabled); // WhatsAppPlugin.vocore2enabled
            this.AttachDelegate("sender", () => _overlaySender); // WhatsAppPlugin.sender
            this.AttachDelegate("typemessage", () => _overlayTypeMessage); // WhatsAppPlugin.typemessage
            this.AttachDelegate("totalmessages", () => _overlayTotalMessages); // WhatsAppPlugin.totalmessages

            // Array de 10 mensagens: WhatsAppPlugin.message[0] a WhatsAppPlugin.message[9]
            for (int i = 0; i < 10; i++)
            {
                int index = i; // catch value for closure
                this.AttachDelegate($"message[{index}]", () => _overlayMessages[index]);
            }

            // ===== LED SLOT PROPERTIES =====
            // Register color properties for LED effects: WhatsAppPlugin.S0_Led1..S7_Led128
            if (_ledManager != null)
            {
                for (int slot = 0; slot < 8; slot++)
                {
                    for (int led = 0; led < 128; led++)
                    {
                        int s = slot;
                        int l = led;
                        this.AttachDelegate($"S{s}_Led{l + 1}", () => _ledManager.GetSlotColor(s, l));
                    }
                }
            }
        }

        private void RegisterActions()
        {
            WriteLog("[ACTIONS] 🔧 Starting RegisterActions()...");

            // 🎮 Actions - appear in Controls & Events
            // IMPORTANT: SimHub automatically adds "WhatsAppPlugin." as prefix!
            // So we register "SendReply1" and SimHub transforms it to "WhatsAppPlugin.SendReply1"
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

                // ✅ Use message that is SHOWING on screen now!
                if (_currentMessageGroup == null || _currentMessageGroup.Count == 0)
                {
                    WriteLog($"[QUICK REPLY] ❌ No message displayed");
                    return;
                }

                // 🔒 ONE-SHOT: Check if already sent reply for this message
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

                // Remove messages (automatic, always removes)
                _messageQueue.RemoveMessagesFromContact(_currentContactRealNumber);
                WriteLog($"[QUICK REPLY {replyNumber}] 🗑️ Removed messages from {contactName} (number: {_currentContactRealNumber})");

                // Show confirmation if configured
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
        /// Show "No connection" message on overlay
        /// </summary>
        private void ShowNoConnectionMessage()
        {
            _showMessage = true;
            _overlaySender = "No connection to WhatsApp";
            _overlayTypeMessage = "";
            _overlayTotalMessages = 1;
            ClearAllOverlayMessages();
            WriteLog("[OVERLAY] Showing 'No connection' message");
        }

        /// <summary>
        /// Show "Scan QR Code" message on overlay
        /// </summary>
        private void ShowQrCodeMessage()
        {
            _showMessage = true;
            _overlaySender = "Scan QR Code in SimHub";
            _overlayTypeMessage = "";
            _overlayTotalMessages = 1;
            ClearAllOverlayMessages();
            WriteLog("[OVERLAY] Showing 'Scan QR Code' message");
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

            // Header - Sender (with +X if there are more messages in queue to show)
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

            // Messages (array of 10, ordered by timestamp)
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
        /// Updates overlay properties to show messages on dashboard
        /// LEGACY: Use version with List<QueuedMessage> when possible
        /// </summary>
        private void UpdateOverlayProperties(QueuedMessage message)
        {
            // 🔒 IGNORE during test - DO NOT CHANGE ANYTHING!
            if (_isTestingMessage) return;

            if (message == null)
            {
                // Clear overlay when there are no messages
                _overlaySender = "";
                _overlayTypeMessage = "";
                _overlayTotalMessages = 0;
                for (int i = 0; i < 10; i++)
                {
                    _overlayMessages[i] = "";
                }
                return;
            }

            // Get message group from this person (same number)
            var groupedMessages = _messageQueue
                .GetAllMessages()
                .Where(m => m.Number == message.Number)
                .OrderBy(m => m.Timestamp)
                .Take(10)
                .ToList();

            // Header - Sender (with +X if there are more messages in queue to show)
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

            // Messages (array of 10)
            for (int i = 0; i < 10; i++)
            {
                if (i < groupedMessages.Count)
                {
                    var msg = groupedMessages[i];
                    _overlayMessages[i] = FormatMessageForOverlay(msg);
                }
                else
                {
                    _overlayMessages[i] = ""; // Clear empty messages
                }
            }

            WriteLog($"[OVERLAY] Updated {_overlaySender} ({_overlayTotalMessages} messages)");
        }

        /// <summary>
        /// Formats message for overlay: "HH:mm [message up to 47 chars or 44 + ...]"
        /// </summary>
        private string FormatMessageForOverlay(QueuedMessage msg)
        {
            string timeStr = msg.Timestamp.ToString("HH:mm"); // 5 chars
            string body = msg.Body;

            // CORRECT limit: time (5) + space (1) + message (47) = 53 chars
            // If truncated: time (5) + space (1) + text (44) + "..." (3) = 53 chars
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
            WriteLog("QR Code received - user needs to scan");
            _isWaitingForQrCode = true;
            StopReconnectTimer(); // Don't try to reconnect while waiting for QR scan
            _settingsControl?.UpdateQRCode(qrCode);
            _settingsControl?.UpdateConnectionStatus("QR");
            ShowQrCodeMessage();
        }

        private void NodeManager_OnReady(object sender, (string number, string name) e)
        {
            _connectionStatus = "Connected";
            _connectedNumber = e.number;
            _isWhatsAppConnected = true;
            _isWaitingForQrCode = false;
            _userRequestedDisconnect = false;
            _settingsControl?.UpdateConnectionStatus("Connected", e.number);

            // Stop reconnection timer on successful connection
            StopReconnectTimer();
            _reconnectAttempts = 0;

            // Clear overlay
            _showMessage = false;
            _overlaySender = "";
            ClearAllOverlayMessages();

            // Resume message queue
            _messageQueue?.ResumeQueue();
            WriteLog("Queue resumed after successful connection");

            // Hide disconnect warning


            // Request Google Contacts status
            GoogleGetStatus();

            WriteLog($"Connected to WhatsApp as {e.number}");
        }

        private void NodeManager_OnMessage(object sender, JObject messageData)
        {
            try
            {
                WriteLog($"Message received from WhatsApp: {messageData}");

                // ⭐ Node.js sends data DIRECTLY (not inside "message")
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

                // Normalize number (remove +, spaces, hyphens)
                var normalizedNumber = number.Replace("+", "").Replace(" ", "").Replace("-", "");

                // If LID, also extract LID from chatId for matching
                string lidNumber = null;
                if (isLid && !string.IsNullOrEmpty(chatId))
                {
                    // chatId comes as "94266210652201@lid"
                    lidNumber = chatId.Split('@')[0];
                    WriteLog($"📞 LID detected - Number: '{number}', ChatId: '{chatId}', LID: '{lidNumber}'");
                }
                else
                {
                    WriteLog($"📞 Received number: '{number}' → Normalized: '{normalizedNumber}'");
                }

                // ⭐ CHECK IF FROM ALLOWED CONTACT!
                WriteLog($"🔍 Checking against {_settings.Contacts.Count} contacts in allowed list:");

                Contact allowedContact = null;

                foreach (var c in _settings.Contacts)
                {
                    var contactNumber = c.Number.Replace("+", "").Replace(" ", "").Replace("-", "");

                    // Try normal match
                    bool matchesNumber = contactNumber == normalizedNumber;

                    // If LID, also try match with LID
                    bool matchesLid = isLid && lidNumber != null && contactNumber == lidNumber;

                    // If LID, also try match with full chatId
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

                // ✅ Allowed contact!
                WriteLog($"✅ ACCEPTED: Contact found in list: {allowedContact.Name} (VIP: {allowedContact.IsVip})");

                // ⭐ USE CONTACT NAME (not "from" from WhatsApp which can be LinkedID)
                string displayName = allowedContact.Name;
                bool isVip = allowedContact.IsVip;

                // Check if contains urgent keywords
                bool isUrgent = _settings.Keywords.Any(keyword =>
                    body.ToLowerInvariant().Contains(keyword.ToLowerInvariant()));

                if (isUrgent)
                {
                    WriteLog($"Message marked as URGENT (keyword detected)");
                }

                // ⭐ CREATE MESSAGE WITH CONTACT NAME
                var queuedMessage = new QueuedMessage
                {
                    From = displayName,  // ⭐ Contact name from list!
                    Number = number,
                    Body = body,
                    ChatId = chatId,
                    IsVip = isVip,
                    IsUrgent = isUrgent
                };

                WriteLog($"✅ QUEUED: From='{displayName}', VIP={isVip}, Urgent={isUrgent}");

                // Add to queue
                _messageQueue.AddMessage(queuedMessage);

            }
            catch (Exception ex)
            {
                WriteLog($"ERROR processing message: {ex.Message}");
            }
        }

        private void NodeManager_OnError(object sender, EventArgs e)
        {
            WriteLog("Connection error detected");

            // Mark WhatsApp as disconnected
            _isWhatsAppConnected = false;

            // If waiting for QR code scan, don't auto-reconnect
            if (_isWaitingForQrCode)
            {
                WriteLog("Waiting for QR code scan - not starting reconnection timer");
                return;
            }

            // If user clicked Disconnect button, don't auto-reconnect
            if (_userRequestedDisconnect)
            {
                WriteLog("User requested disconnect - not starting reconnection timer");
                _connectionStatus = "Disconnected";
                _settingsControl?.UpdateConnectionStatus("Disconnected");
                ClearOverlayProperties();
                return;
            }

            // Pause message queue during reconnection
            _messageQueue?.PauseQueue();
            WriteLog("Queue paused for reconnection");

            // Start reconnection timer (15 seconds interval)
            StartReconnectTimer();
        }

        /// <summary>
        /// Start the 15-second reconnection timer
        /// </summary>
        private void StartReconnectTimer()
        {
            // Don't start if already running
            if (_reconnectTimer != null && _reconnectTimer.Enabled)
            {
                WriteLog("Reconnection timer already running");
                return;
            }

            _reconnectAttempts = 0;
            _connectionStatus = "Reconnecting...";
            _settingsControl?.UpdateConnectionStatus("Reconnecting (1/5)...");

            WriteLog($"Starting reconnection timer ({RECONNECT_INTERVAL_MS}ms interval, max {MAX_RECONNECT_ATTEMPTS} attempts)");

            _reconnectTimer = new System.Timers.Timer(RECONNECT_INTERVAL_MS);
            _reconnectTimer.Elapsed += ReconnectTimer_Elapsed;
            _reconnectTimer.AutoReset = true;
            _reconnectTimer.Start();

            // Try first reconnection immediately
            TryReconnect();
        }

        /// <summary>
        /// Stop the reconnection timer
        /// </summary>
        private void StopReconnectTimer()
        {
            if (_reconnectTimer != null)
            {
                _reconnectTimer.Stop();
                _reconnectTimer.Elapsed -= ReconnectTimer_Elapsed;
                _reconnectTimer.Dispose();
                _reconnectTimer = null;
                WriteLog("Reconnection timer stopped");
            }
        }

        /// <summary>
        /// Timer callback - check connection and retry if needed
        /// </summary>
        private void ReconnectTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Check if already connected
            if (_isWhatsAppConnected)
            {
                WriteLog("Already connected - stopping reconnection timer");
                StopReconnectTimer();
                return;
            }

            // Check if user requested disconnect
            if (_userRequestedDisconnect)
            {
                WriteLog("User requested disconnect - stopping reconnection timer");
                StopReconnectTimer();
                _connectionStatus = "Disconnected";
                _settingsControl?.UpdateConnectionStatus("Disconnected");
                return;
            }

            // Try to reconnect
            TryReconnect();
        }

        /// <summary>
        /// Attempt to reconnect to WhatsApp
        /// </summary>
        private async void TryReconnect()
        {
            _reconnectAttempts++;

            if (_reconnectAttempts > MAX_RECONNECT_ATTEMPTS)
            {
                // All attempts failed
                WriteLog($"All {MAX_RECONNECT_ATTEMPTS} reconnection attempts failed");
                StopReconnectTimer();

                _connectionStatus = "Connection Error";
                _settingsControl?.UpdateConnectionStatus("Connection Error");

                // Show error message on screen
                ShowNoConnectionMessage();
                return;
            }

            WriteLog($"Reconnection attempt {_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS}");
            _connectionStatus = $"Reconnecting ({_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS})...";
            _settingsControl?.UpdateConnectionStatus($"Reconnecting ({_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS})...");

            try
            {
                // Stop current connection
                _nodeManager?.Stop();
                await Task.Delay(1000);

                // Start new connection
                await _nodeManager.StartAsync();
                // If successful, NodeManager_OnReady will be called and stop the timer
            }
            catch (Exception ex)
            {
                WriteLog($"Reconnection attempt {_reconnectAttempts} failed: {ex.Message}");
                // Timer will trigger next attempt in 15 seconds
            }
        }

        private void NodeManager_OnStatusChanged(object sender, string status)
        {
            WriteLog($"📡 Status changed: {status}");

            if (status == "Starting")
            {
                _connectionStatus = "Starting Node.js...";
                _settingsControl?.UpdateConnectionStatus("Connecting");
            }
            else if (status == "Connected")
            {
                // Do nothing, Ready event will handle it
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
                // Log but don't update UI
                WriteLog($"🔍 {status}");

                // If script was updated, refresh version in UI
                if (status.Contains("Updated script") || status.Contains("Created script"))
                {
                    _settingsControl?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        _settingsControl?.RefreshScriptsVersion();
                    }));
                }
            }
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
                WriteLog($"Error disconnecting from Google: {ex.Message}");
            }
        }

        #endregion

        #region WhatsApp Number Verification

        /// <summary>
        /// Check if a phone number has WhatsApp
        /// </summary>
        public async void CheckWhatsAppNumber(string number)
        {
            WriteLog($"Checking if {number} has WhatsApp...");
            try
            {
                if (_nodeManager != null)
                {
                    await _nodeManager.SendJsonAsync(new { type = "checkWhatsApp", number = number });
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Error checking WhatsApp number: {ex.Message}");
            }
        }

        private void NodeManager_OnCheckWhatsAppResult(object sender, (string number, bool exists, string error) result)
        {
            WriteLog($"WhatsApp check result: {result.number} - exists: {result.exists}, error: {result.error}");
            _settingsControl?.HandleCheckWhatsAppResult(result.number, result.exists, result.error);
        }

        #endregion

        #region Plugin Auto-Update

        private string _latestVersion = null;
        private string _downloadUrl = null;
        private bool _isDownloading = false;

        /// <summary>
        /// Check GitHub for new plugin version
        /// </summary>
        public async Task CheckForPluginUpdateAsync()
        {
            try
            {
                WriteLog("Checking for plugin updates...");
                _settingsControl?.UpdatePluginUpdateStatus("Checking...", "#858585");

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "WhatsAppSimHubPlugin");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var response = await client.GetStringAsync(
                        $"https://api.github.com/repos/{GITHUB_REPO}/releases/latest");

                    var json = JObject.Parse(response);
                    var tagName = json["tag_name"]?.ToString();
                    var assets = json["assets"] as JArray;

                    if (string.IsNullOrEmpty(tagName))
                    {
                        WriteLog("No release tag found");
                        _settingsControl?.UpdatePluginUpdateStatus("✓ Up to date", "#0E7A0D");
                        return;
                    }

                    // Clean version (remove 'v' prefix if present)
                    _latestVersion = tagName.TrimStart('v', 'V');

                    // Find DLL asset
                    _downloadUrl = null;
                    if (assets != null)
                    {
                        foreach (var asset in assets)
                        {
                            var name = asset["name"]?.ToString();
                            if (name != null && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            {
                                _downloadUrl = asset["browser_download_url"]?.ToString();
                                break;
                            }
                        }
                    }

                    // Compare versions
                    if (IsNewerVersion(_latestVersion, PLUGIN_VERSION))
                    {
                        WriteLog($"New version available: {_latestVersion} (current: {PLUGIN_VERSION})");
                        _settingsControl?.ShowPluginUpdateAvailable(_latestVersion);
                    }
                    else
                    {
                        WriteLog($"Plugin is up to date ({PLUGIN_VERSION})");
                        _settingsControl?.UpdatePluginUpdateStatus("✓ Up to date", "#0E7A0D");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Error checking for updates: {ex.Message}");
                _settingsControl?.UpdatePluginUpdateStatus("✓ Up to date", "#0E7A0D");
            }
        }

        /// <summary>
        /// Compare two version strings (e.g., "1.1.0" > "1.0.0")
        /// </summary>
        private bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                var newParts = newVersion.Split('.').Select(int.Parse).ToArray();
                var currentParts = currentVersion.Split('.').Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Max(newParts.Length, currentParts.Length); i++)
                {
                    int newPart = i < newParts.Length ? newParts[i] : 0;
                    int currentPart = i < currentParts.Length ? currentParts[i] : 0;

                    if (newPart > currentPart) return true;
                    if (newPart < currentPart) return false;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Download the new plugin DLL from GitHub
        /// </summary>
        public async Task DownloadPluginUpdateAsync()
        {
            if (_isDownloading || string.IsNullOrEmpty(_downloadUrl))
                return;

            _isDownloading = true;

            try
            {
                WriteLog($"Downloading plugin update from: {_downloadUrl}");
                _settingsControl?.UpdatePluginUpdateStatus("Downloading...", "#007ACC");

                var updatesPath = Path.Combine(_pluginPath, "updates");
                if (!Directory.Exists(updatesPath))
                    Directory.CreateDirectory(updatesPath);

                var dllPath = Path.Combine(updatesPath, "WhatsAppSimHubPlugin.dll");

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "WhatsAppSimHubPlugin");

                    var response = await client.GetAsync(_downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var downloadedBytes = 0L;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(dllPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                var progress = (int)((downloadedBytes * 100) / totalBytes);
                                _settingsControl?.UpdatePluginUpdateStatus($"Downloading... {progress}%", "#007ACC");
                            }
                        }
                    }
                }

                WriteLog("Plugin update downloaded successfully");
                _settingsControl?.ShowPluginUpdateReady(_latestVersion);
            }
            catch (Exception ex)
            {
                WriteLog($"Error downloading update: {ex.Message}");
                _settingsControl?.UpdatePluginUpdateStatus("Download failed", "#C42B1C");
            }
            finally
            {
                _isDownloading = false;
            }
        }

        /// <summary>
        /// Install the downloaded update (creates batch file and closes SimHub)
        /// </summary>
        public void InstallPluginUpdate()
        {
            try
            {
                var updatesPath = Path.Combine(_pluginPath, "updates");
                var newDllPath = Path.Combine(updatesPath, "WhatsAppSimHubPlugin.dll");

                if (!File.Exists(newDllPath))
                {
                    WriteLog("No update file found to install");
                    return;
                }

                // Get SimHub paths
                var simhubPath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location)
                    ?? @"C:\Program Files (x86)\SimHub";
                var targetDllPath = Path.Combine(simhubPath, "WhatsAppSimHubPlugin.dll");
                var simhubExe = Path.Combine(simhubPath, "SimHubWPF.exe");

                // Path to node scripts
                var nodeScriptsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SimHub", "WhatsAppPlugin", "node");

                // Create batch file for update
                var batPath = Path.Combine(updatesPath, "update.bat");
                var batContent = $@"@echo off
echo Updating WhatsApp Plugin...
echo Waiting for SimHub to close...
timeout /t 5 /nobreak > nul

echo Removing old plugin...
del ""{targetDllPath}"" 2>nul

echo Removing old scripts (will be recreated from new DLL)...
del ""{nodeScriptsPath}\whatsapp-server.js"" 2>nul
del ""{nodeScriptsPath}\baileys-server.mjs"" 2>nul
del ""{nodeScriptsPath}\google-contacts.js"" 2>nul

echo Installing new version...
copy ""{newDllPath}"" ""{targetDllPath}""

if exist ""{targetDllPath}"" (
    echo Update successful!
    del ""{newDllPath}"" 2>nul
    echo Starting SimHub...
    start """" ""{simhubExe}""
) else (
    echo Update failed! Please copy manually.
    pause
)

del ""%~f0""
";

                File.WriteAllText(batPath, batContent);
                WriteLog($"Update batch file created: {batPath}");

                // Start the batch file hidden
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = batPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    }
                };
                process.Start();

                WriteLog("Update process started, closing SimHub...");

                // Close SimHub
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                WriteLog($"Error installing update: {ex.Message}");
                _settingsControl?.ShowToast($"Error installing update: {ex.Message}", "❌", 10);
            }
        }

        /// <summary>
        /// Check if there's a pending update that failed to install
        /// </summary>
        private void CheckPendingUpdate()
        {
            try
            {
                var updatesPath = Path.Combine(_pluginPath, "updates");
                var pendingDll = Path.Combine(updatesPath, "WhatsAppSimHubPlugin.dll");

                if (File.Exists(pendingDll))
                {
                    WriteLog("Found pending update, cleaning up...");
                    // Clean up - the batch file should have handled it
                    try { File.Delete(pendingDll); } catch { }
                }
            }
            catch { }
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
                _currentContactRealNumber = messages[0].Number;  // ⭐ Real number to send!

                WriteLog($"[EVENT] OnGroupDisplay: Saved chatId = {messages[0].ChatId}, realNumber = {messages[0].Number}");

                WriteLog($"[EVENT] Calling UpdateOverlayProperties with {messages.Count} messages...");

                // ✅ ATUALIZAR OVERLAY
                UpdateOverlayProperties(messages);

                // Play sound notification (only once per contact)
                if (_soundManager != null)
                {
                    string contactNumber = messages[0].Number;
                    bool isNewContact = _lastSoundPlayedForContact != contactNumber;
                    bool hasUrgent = messages.Any(m => m.IsUrgent);

                    // Play sound for new contacts OR if urgent messages arrive (always play urgent)
                    if (isNewContact || hasUrgent)
                    {
                        string soundFile = null;
                        bool shouldPlaySound = false;

                        // Priority order: Urgent > VIP > Normal
                        if (hasUrgent && _settings?.UrgentSoundEnabled == true)
                        {
                            soundFile = _settings.UrgentSoundFile;
                            shouldPlaySound = true;
                        }
                        else if (messages.Any(m => m.IsVip) && _settings?.VipSoundEnabled == true)
                        {
                            soundFile = _settings.VipSoundFile;
                            shouldPlaySound = true;
                        }
                        else if (isNewContact && _settings?.NormalSoundEnabled == true)
                        {
                            soundFile = _settings.NormalSoundFile;
                            shouldPlaySound = true;
                        }

                        if (shouldPlaySound && !string.IsNullOrEmpty(soundFile))
                        {
                            // MediaPlayer must be called from UI thread
                            _settingsControl?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                _soundManager.PlaySound(soundFile);
                            }));

                            _lastSoundPlayedForContact = contactNumber;
                            WriteLog($"[SOUND] Played sound for contact {contactNumber}{(hasUrgent ? " (urgent)" : "")}");
                        }
                    }
                    else
                    {
                        WriteLog($"[SOUND] Skipped sound - already played for contact {contactNumber}");
                    }
                }

                // Trigger LED notification effects
                if (_ledManager != null && _settings?.LedEffectsEnabled == true)
                {
                    string contactNumber = messages[0].Number;
                    bool isNewContact = _lastLedPlayedForContact != contactNumber;
                    bool hasUrgent = messages.Any(m => m.IsUrgent);

                    if (isNewContact || hasUrgent)
                    {
                        string priority = null;
                        int durationMs = _settings.NormalDuration;

                        if (hasUrgent && _settings.LedUrgentEnabled)
                        {
                            priority = "urgent";
                            durationMs = _settings.UrgentDuration;
                        }
                        else if (messages.Any(m => m.IsVip) && _settings.LedVipEnabled)
                        {
                            priority = "vip";
                            durationMs = _settings.UrgentDuration;
                        }
                        else if (isNewContact && _settings.LedNormalEnabled)
                        {
                            priority = "normal";
                            durationMs = _settings.NormalDuration;
                        }

                        if (priority != null)
                        {
                            _ledManager.TriggerEffect(_settings.LedDevices, priority, durationMs);
                            _lastLedPlayedForContact = contactNumber;
                            WriteLog($"[LED] Triggered {priority} effect for contact {contactNumber} ({durationMs}ms)");
                        }
                    }
                }

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
            _lastSoundPlayedForContact = ""; // Reset sound tracking when message is removed
            _lastLedPlayedForContact = ""; // Reset LED tracking when message is removed
            _ledManager?.StopAllEffects();

            WriteLog($"[EVENT] Calling UpdateOverlayProperties(null) to clear overlay...");

            // ✅ LIMPAR OVERLAY
            UpdateOverlayProperties((List<QueuedMessage>)null);

            WriteLog($"[EVENT] OnMessageRemoved completed - overlay cleared");
        }

        public void End(PluginManager pluginManager)
        {
            WriteLog("=== WhatsApp Plugin Shutting Down ===");

            SaveSettings();

            // Stop reconnection timer
            StopReconnectTimer();

            // Kill Node.js and all child processes (Chrome/Puppeteer)
            if (_nodeManager != null)
            {
                WriteLog("Killing Node.js process tree...");
                _nodeManager.Stop();
                _nodeManager.Dispose();
            }

            _messageQueue?.Dispose();
            _soundManager?.Dispose();
            _ledManager?.Dispose();

            WriteLog("Plugin shutdown complete");
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    // ✅ File exists - load WITHOUT modifying
                    var json = File.ReadAllText(_settingsFile);
                    _settings = JsonConvert.DeserializeObject<PluginSettings>(json);

                    // DO NOT call EnsureDefaults() here!
                    // Settings already exist, don't modify!
                }
                else
                {
                    // ✅ First time - create new settings WITH defaults
                    _settings = new PluginSettings();
                    _settings.EnsureDefaults();
                    SaveSettings(); // Save immediately to create the file
                }

                // Sync VoCore enabled properties based on configured devices
                _voCore1Enabled = !string.IsNullOrEmpty(_settings.VoCore1_Serial);
                _voCore2Enabled = !string.IsNullOrEmpty(_settings.VoCore2_Serial);
                _voCoreEnabled = _voCore1Enabled || _voCore2Enabled;
            }
            catch (Exception)
            {
                // ⚠️ Error reading - create new
                _settings = new PluginSettings();
                _settings.EnsureDefaults();
                SaveSettings();
                _voCore1Enabled = false;
                _voCore2Enabled = false;
                _voCoreEnabled = false;
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
                _settingsControl.SetSoundManager(_soundManager);
            }
            return _settingsControl;
        }

        // Public methods for the UI
        public void DisconnectWhatsApp()
        {
            // Log stack trace to find who called this
            var stackTrace = new System.Diagnostics.StackTrace(true);
            WriteLog($"User requested disconnect - Called from:\n{stackTrace}");

            // Mark as user requested disconnect (prevents auto-reconnect)
            _userRequestedDisconnect = true;
            _reconnectAttempts = 0;
            StopReconnectTimer();

            // Clear message queues
            _messageQueue?.ClearQueue();
            WriteLog("Queues cleared on user disconnect");

            // Clear overlay
            _showMessage = false;
            _overlaySender = "";
            ClearAllOverlayMessages();

            _nodeManager?.Stop();
        }

        public async System.Threading.Tasks.Task ReconnectWhatsApp()
        {
            WriteLog("User requested connect...");

            // Reset flags - user wants to connect
            _userRequestedDisconnect = false;
            _reconnectAttempts = 0;
            StopReconnectTimer();

            // Clear overlay
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

                // DON'T show MessageBox - only log!
            }
        }

        /// <summary>
        /// Retry npm install: kills Node, cleans caches, deletes node_modules, re-runs npm install
        /// Called from Retry Install button in UI
        /// </summary>
        public async Task RetryNpmInstall()
        {
            WriteLog("=== Retry npm install requested ===");

            try
            {
                _nodeManager?.Stop();

                _settingsControl?.SetDependenciesInstalling(true, "Reinstalling npm packages...");
                _settingsControl?.UpdateNpmStatus("Installing...", false);

                bool success = await _dependencyManager.EnsureNpmPackages(forceReinstall: true).ConfigureAwait(false);

                if (success)
                {
                    WriteLog("npm install completed successfully!");
                    _settingsControl?.UpdateNpmStatus("Installed", true);
                    _settingsControl?.HideRetryInstallButton();
                }
                else
                {
                    WriteLog("npm install failed!");
                    _settingsControl?.UpdateNpmStatus("Installation failed", false, true);
                    _settingsControl?.ShowRetryInstallButton();
                }

                _settingsControl?.SetDependenciesInstalling(false);
            }
            catch (Exception ex)
            {
                WriteLog($"Retry npm install error: {ex.Message}");
                _settingsControl?.SetDependenciesInstalling(false);
                _settingsControl?.ShowRetryInstallButton();
            }
        }

        /// <summary>
        /// Switches backend (whatsapp-web.js <-> Baileys) and auto-reconnects
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
                    _nodeManager.GoogleStatusReceived -= NodeManager_OnGoogleStatusReceived;
                    _nodeManager.GoogleAuthUrlReceived -= NodeManager_OnGoogleAuthUrlReceived;
                    _nodeManager.GoogleContactsDone -= NodeManager_OnGoogleContactsDone;
                    _nodeManager.GoogleError -= NodeManager_OnGoogleError;
                    _nodeManager.CheckWhatsAppResult -= NodeManager_OnCheckWhatsAppResult;
                }

                // 4. Create new nodeManager with chosen backend
                WriteLog($"Creating new WebSocketManager with backend: {newBackend}");
                _nodeManager = new WebSocketManager(_pluginPath, newBackend);
                _nodeManager.OnQrCode += NodeManager_OnQrCode;
                _nodeManager.OnReady += NodeManager_OnReady;
                _nodeManager.OnMessage += NodeManager_OnMessage;
                _nodeManager.OnError += NodeManager_OnError;
                _nodeManager.StatusChanged += NodeManager_OnStatusChanged;
                _nodeManager.GoogleStatusReceived += NodeManager_OnGoogleStatusReceived;
                _nodeManager.GoogleAuthUrlReceived += NodeManager_OnGoogleAuthUrlReceived;
                _nodeManager.GoogleContactsDone += NodeManager_OnGoogleContactsDone;
                _nodeManager.GoogleError += NodeManager_OnGoogleError;
                _nodeManager.CheckWhatsAppResult += NodeManager_OnCheckWhatsAppResult;

                // 5. Start the new backend
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



        /// <summary>
        /// Apply dashboard directly (1 layer mode) - called from UI
        /// </summary>
        public void ApplyDashboardDirect(int vocoreNumber, string dashboardName)
        {
            string serial = vocoreNumber == 1 ? _settings?.VoCore1_Serial : _settings?.VoCore2_Serial;
            if (string.IsNullOrEmpty(serial))
                return;

            _vocoreManager?.SetDashboard(serial, vocoreNumber, dashboardName);
            WriteLog($"[ApplyDirect] VoCore {vocoreNumber}: '{dashboardName}'");
        }

        /// <summary>
        /// Apply merged dashboard (2 layers mode) - called from UI
        /// </summary>
        public void ApplyDashboardMerged(int vocoreNumber, string layer1Dashboard, string layer2Dashboard)
        {
            string serial = vocoreNumber == 1 ? _settings?.VoCore1_Serial : _settings?.VoCore2_Serial;
            if (string.IsNullOrEmpty(serial))
                return;

            _vocoreManager?.ApplyMerged(serial, vocoreNumber, layer1Dashboard, layer2Dashboard);
            WriteLog($"[ApplyMerged] VoCore {vocoreNumber}: '{layer1Dashboard}' + '{layer2Dashboard}'");
        }

        /// <summary>
        /// Check if merged dashboard exists for a VoCore
        /// </summary>
        public bool DoesMergedDashboardExist(int vocoreNumber)
        {
            return _vocoreManager?.MergedDashboardExists(vocoreNumber) ?? false;
        }

        /// <summary>
        /// Clear the overlay dashboard for a VoCore (set to empty)
        /// Called when user deselects a VoCore from a slot
        /// </summary>
        public void ClearOverlayDashboard(string serialNumber)
        {
            WriteLog($"[ClearOverlay] Called with serial: '{serialNumber}'");
            if (string.IsNullOrEmpty(serialNumber))
            {
                WriteLog($"[ClearOverlay] Serial is empty, skipping");
                return;
            }

            _vocoreManager?.ClearOverlayDashboard(serialNumber);
        }

        /// <summary>
        /// Apply the correct dashboard based on saved settings for a VoCore slot
        /// Called when user selects a VoCore for a slot
        /// </summary>
        public void ApplyDashboardFromSettings(int vocoreNumber)
        {
            string serial = vocoreNumber == 1 ? _settings?.VoCore1_Serial : _settings?.VoCore2_Serial;
            if (string.IsNullOrEmpty(serial))
                return;

            int layerCount = vocoreNumber == 1 ? _settings.VoCore1_LayerCount : _settings.VoCore2_LayerCount;
            string defaultDash = vocoreNumber == 1 ? "WhatsAppPluginVocore1" : "WhatsAppPluginVocore2";

            if (layerCount == 2)
            {
                // 2 layers - set merged dashboard
                string mergedDash = DashboardMerger.GetMergedDashboardName(vocoreNumber);
                _vocoreManager.SetDashboard(serial, vocoreNumber, mergedDash);
                WriteLog($"[ApplyFromSettings] VoCore {vocoreNumber}: Dashboard set to '{mergedDash}'");
            }
            else
            {
                // 1 layer - use Mode1 dashboard
                string mode1Dash = vocoreNumber == 1 ? _settings.VoCore1_Mode1_Dash : _settings.VoCore2_Mode1_Dash;
                if (string.IsNullOrEmpty(mode1Dash)) mode1Dash = defaultDash;
                _vocoreManager.SetDashboard(serial, vocoreNumber, mode1Dash);
                WriteLog($"[ApplyFromSettings] VoCore {vocoreNumber}: Dashboard set to '{mode1Dash}'");
            }
        }

        /// <summary>
        /// Set VoCore 1 enabled state (called from UI when device is selected/deselected)
        /// </summary>
        public void SetVoCore1Enabled(bool enabled)
        {
            _voCore1Enabled = enabled;
            UpdateLegacyVoCoreEnabled();
            WriteLog($"VoCore 1 enabled: {enabled}");
        }

        /// <summary>
        /// Set VoCore 2 enabled state (called from UI when device is selected/deselected)
        /// </summary>
        public void SetVoCore2Enabled(bool enabled)
        {
            _voCore2Enabled = enabled;
            UpdateLegacyVoCoreEnabled();
            WriteLog($"VoCore 2 enabled: {enabled}");
        }

        /// <summary>
        /// Update legacy vocoreenabled property (= vocore1enabled || vocore2enabled)
        /// </summary>
        private void UpdateLegacyVoCoreEnabled()
        {
            _voCoreEnabled = _voCore1Enabled || _voCore2Enabled;
            _settings.VoCoreEnabled = _voCoreEnabled;
        }

        /// <summary>
        /// Get the path to SimHub's DashTemplates folder
        /// </summary>
        public string GetDashboardsPath()
        {
            return _dashboardInstaller?.GetDashboardsPath();
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
        /// 🎮 Method called automatically by SimHub at 60 FPS!
        ///
        /// ✅ QUICK REPLIES: Native SimHub system with ControlsEditor + Actions!
        ///
        /// ControlsEditor automatically binds buttons to registered Actions.
        /// When user presses the button, SimHub calls the Action directly.
        /// Automatic VoCore check every 3 seconds
        /// </summary>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Update LED effects every frame (60 FPS) for smooth animations
            _ledManager?.Update();

            // Only check every 3 seconds (not every frame!)
            if ((DateTime.Now - _lastDataUpdateCheck).TotalMilliseconds < DATA_UPDATE_INTERVAL_MS)
                return;

            _lastDataUpdateCheck = DateTime.Now;

            try
            {
                // Refresh device list in UI (detects VoCore connect/disconnect)
                _settingsControl?.RefreshDeviceList();

                // Refresh dashboard dropdowns if new dashboards were installed
                _settingsControl?.RefreshDashboardList();

                // Install default dashboards if missing (uses SimHub API - zero I/O check)
                EnsureDashboardsInstalled();

                // Ensure overlay is ON for configured VoCores
                if (!string.IsNullOrEmpty(_settings?.VoCore1_Serial))
                    _vocoreManager?.EnsureOverlayEnabled(_settings.VoCore1_Serial);

                if (!string.IsNullOrEmpty(_settings?.VoCore2_Serial))
                    _vocoreManager?.EnsureOverlayEnabled(_settings.VoCore2_Serial);

                // Check if Color Effects profiles changed and re-inject LED containers
                _ledManager?.CheckProfileChanges();

                // Refresh LED devices (detects connect/disconnect, returns early if no changes)
                _settingsControl?.RefreshLedDevices();
            }
            catch
            {
                // Silence errors - we don't want spam in log every 3s
                // VoCoreManager already does its own logging
            }
        }

        /// <summary>
        /// Ensure all required dashboards are installed (uses SimHub API for checking)
        /// </summary>
        private void EnsureDashboardsInstalled()
        {
            if (_vocoreManager == null || _dashboardInstaller == null) return;

            // Check and install WhatsAppPluginVocore1
            if (!_vocoreManager.DoesDashboardExist("WhatsAppPluginVocore1"))
            {
                _dashboardInstaller.InstallDashboard("WhatsAppPluginVocore1.simhubdash");
            }

            // Check and install WhatsAppPluginVocore2
            if (!_vocoreManager.DoesDashboardExist("WhatsAppPluginVocore2"))
            {
                _dashboardInstaller.InstallDashboard("WhatsAppPluginVocore2.simhubdash");
            }

            // Check and install VR Overlay dashboard
            if (!_vocoreManager.DoesDashboardExist("Simhub WhatsApp Plugin Overlay"))
            {
                _dashboardInstaller.InstallDashboard("Simhub WhatsApp Plugin Overlay.simhubdash");
            }
        }

        /// <summary>
        /// Shows test message for 5 seconds (doesn't change VoCore or dashboard)
        /// During test, completely ignores both queues
        /// After 5s, CLEARS EVERYTHING so plugin can continue
        /// </summary>

        /// <summary>
        /// Discovers LED devices without connecting (for change detection).
        /// </summary>
        public List<DiscoveredLedDevice> DiscoverLedDevices()
        {
            if (_ledManager == null) return new List<DiscoveredLedDevice>();
            return _ledManager.DiscoverDevices();
        }

        /// <summary>
        /// Full reconnect: disconnects all, discovers and connects.
        /// Called when device list changes.
        /// </summary>
        public List<DiscoveredLedDevice> DiscoverAndConnectLedDevices()
        {
            if (_ledManager == null) return new List<DiscoveredLedDevice>();

            _ledManager.DisconnectAll();
            var devices = _ledManager.DiscoverDevices();

            foreach (var device in devices)
            {
                _ledManager.ConnectDevice(device);
            }

            return devices;
        }

        /// <summary>
        /// Tests a LED effect on a specific device.
        /// </summary>
        public void TestLedEffect(LedDeviceConfig config, string priority)
        {
            if (_ledManager == null || config == null) return;
            _ledManager.TestEffect(config, priority, 10000);
        }

        public void ShowTestMessage(string targetSerial = null)
        {
            try
            {
                WriteLog($"[TEST] ▶ ShowTestMessage started (target: {targetSerial ?? "default"})");

                // 🔥 BLOQUEAR QUEUES durante teste
                _isTestingMessage = true;
                WriteLog($"[TEST] _isTestingMessage = TRUE (queues BLOCKED)");

                // Current time formatted
                string currentTime = DateTime.Now.ToString("HH:mm");

                // ✅ Set private fields directly (exposed via AttachDelegate)
                _showMessage = true;
                _overlaySender = "Bruno Silva";
                _overlayTypeMessage = "VIP"; // Star badge
                _overlayTotalMessages = 1;
                _overlayMessages[0] = $"{currentTime} Hello this is a test :)";
                _overlayMessages[1] = "";
                _overlayMessages[2] = "";
                _overlayMessages[3] = "";
                _overlayMessages[4] = "";

                WriteLog($"✅ Test message displayed: {currentTime} Hello this is a test :)");
                WriteLog($"[TEST] Waiting 5 seconds before clearing...");

                // 🔥 After 5 seconds: CLEAR EVERYTHING and UNBLOCK QUEUES
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

                    // Unblock queues
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
                _isTestingMessage = false; // Ensure unlock in case of error
                WriteLog($"❌ ShowTestMessage error: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows confirmation that quick reply was sent (5s, pauses queue)
        /// </summary>
        public void ShowQuickReplyConfirmation(string recipientName)
        {
            try
            {
                WriteLog($"[CONFIRMATION] ▶ Showing quick reply confirmation for {recipientName}");

                // 🔥 BLOCK QUEUES during confirmation
                _isTestingMessage = true;

                // Current time formatted
                string currentTime = DateTime.Now.ToString("HH:mm");

                // ✅ Show confirmation
                _showMessage = true;
                _overlaySender = recipientName;
                _overlayTypeMessage = ""; // No badge
                _overlayTotalMessages = 1;
                _overlayMessages[0] = $"{currentTime} Quick reply sent successfully";
                _overlayMessages[1] = "";
                _overlayMessages[2] = "";
                _overlayMessages[3] = "";
                _overlayMessages[4] = "";

                WriteLog($"[CONFIRMATION] ✅ Confirmation displayed for {recipientName}");

                // 🔥 After 5 seconds: CLEAR and UNBLOCK
                System.Threading.Tasks.Task.Delay(5000).ContinueWith(_ =>
                {
                    WriteLog($"[CONFIRMATION] ▶ 5 seconds elapsed - clearing confirmation");

                    // Clear overlay
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
                _isTestingMessage = false; // Ensure unlock
                WriteLog($"[CONFIRMATION ERROR] {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all VIP/URGENT messages from queue
        /// Useful when user enables RemoveAfterFirstDisplay
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




        #region Dependency Setup

        /// <summary>
        /// Initializes and checks all dependencies (Node.js, Git, npm packages)
        /// Only starts Node.js after everything is installed
        /// </summary>
        private async Task InitializeDependenciesAsync()
        {
            try
            {
                _dependencyManager = new DependencyManager(_pluginPath);
                _dependencyManager.StatusChanged += (s, msg) => WriteLog(msg);

                // ENSURE SetupControl is ready before starting
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

                    // DISABLE CONNECTION BUTTONS DURING INSTALLATION
                    _settingsControl.Dispatcher.Invoke(() =>
                    {
                        _settingsControl.DisconnectButton.IsEnabled = false;
                        _settingsControl.DisconnectButton.ToolTip = "Installing dependencies...";
                        _settingsControl.ReconnectButton.IsEnabled = false;
                        _settingsControl.ReconnectButton.ToolTip = "Installing dependencies...";
                    });

                    // INITIALIZE ALL STATUS EXPLICITLY
                    _settingsControl.UpdateNodeStatus("Checking...", false);
                    _settingsControl.UpdateGitStatus("Waiting...", false);
                    _settingsControl.UpdateNpmStatus("Waiting...", false);
                    // _settingsControl.UpdateProgress(0, "Checking dependencies...");

                    // Small delay for UI to render
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

                        // Wait 500ms for filesystem to update
                        await Task.Delay(500).ConfigureAwait(false);

                        // VERIFY if installed
                        WriteLog("Verifying Node.js installation...");
                        bool verifyInstalled = _dependencyManager.IsNodeInstalled();

                        if (verifyInstalled)
                        {
                            WriteLog("✅ Node.js files verified!");

                            // TEST REAL EXECUTION AND CAPTURE VERSION!
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
                            // _settingsControl.ShowRetryButton(); // SHOW RETRY BUTTON!
                        }
                        return;
                    }
                }
                else
                {
                    WriteLog("✅ Node.js already installed (found existing installation)!");
                    WriteLog("This could be: portable local, global, or in PATH");
                    WriteLog("No need to install - will use existing Node.js");

                    // TEST if executes AND CAPTURE VERSION!
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

                        // Delay to ensure UI renders
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

                        // Wait 500ms for filesystem to update
                        await Task.Delay(500).ConfigureAwait(false);

                        // VERIFY if installed
                        WriteLog("Verifying Git installation...");
                        bool verifyInstalled = _dependencyManager.IsGitInstalled();

                        if (verifyInstalled)
                        {
                            WriteLog("✅ Git files verified!");

                            // TEST REAL EXECUTION AND CAPTURE VERSION!
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
                            // _settingsControl.ShowRetryButton(); // SHOW RETRY BUTTON!
                        }
                        return;
                    }
                }
                else
                {
                    WriteLog("✅ Git already installed (found existing installation)!");

                    // TEST if executes AND CAPTURE VERSION!
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

                // Force reinstall if Node or Git was just installed
                bool forceReinstall = nodeWasInstalled || gitWasInstalled;
                if (forceReinstall)
                    WriteLog("Node or Git was just installed - forcing npm reinstall...");

                if (_settingsControl != null)
                {
                    _settingsControl.Dispatcher.Invoke(() =>
                    {
                        _settingsControl.UpdateNpmStatus("Checking...", false);
                    });
                }

                // Stop Node.js before any npm operations
                _nodeManager?.Stop();

                bool npmReady = await _dependencyManager.EnsureNpmPackages(forceReinstall).ConfigureAwait(false);

                if (npmReady)
                {
                    WriteLog("✅ npm packages ready!");
                    _settingsControl?.Dispatcher?.Invoke(() =>
                    {
                        _settingsControl.UpdateNpmStatus("Installed", true);
                        _settingsControl.SetDependenciesInstalling(false);
                        _settingsControl.HideRetryInstallButton();
                    });
                }
                else
                {
                    WriteLog("❌ npm packages installation failed!");
                    _settingsControl?.Dispatcher?.Invoke(() =>
                    {
                        _settingsControl.UpdateNpmStatus("Installation failed", false, true);
                        _settingsControl.SetDependenciesInstalling(false);
                    });
                    _settingsControl?.ShowRetryInstallButton();
                    return;
                }

                // ============ ALL READY! ============
                WriteLog("✅ All dependencies ready!");

                // RE-ENABLE CONNECTION BUTTONS
                if (_settingsControl != null)
                {
                    _settingsControl.Dispatcher.Invoke(() =>
                    {
                        _settingsControl.DisconnectButton.IsEnabled = false; // Disconnected state
                        _settingsControl.DisconnectButton.ToolTip = null;
                        _settingsControl.ReconnectButton.IsEnabled = true;   // Allow reconnect
                        _settingsControl.ReconnectButton.ToolTip = null;
                    });
                }

                // Auto-connect: start Node.js
                WriteLog("Starting Node.js...");
                await _nodeManager.StartAsync().ConfigureAwait(false);
                WriteLog("✅ Node.js started successfully!");
            }
            catch (Exception ex)
            {
                WriteLog($"❌ CRITICAL ERROR during dependency setup: {ex.Message}");
                WriteLog($"   Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Tests if Node.js can be executed and captures the version
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

                    // Wait before retry
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
        /// Tests if Git can be executed and captures the version
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
                            // Extract version: "git version 2.47.1.windows.1" → "2.47.1"
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

                    // Wait before retry
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
        /// Event handler when user clicks Retry button in Setup
        /// </summary>

        /// <summary>
        /// Event handler when user clicks Restart SimHub button
        /// </summary>


        #endregion
    }
}
