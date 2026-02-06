using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WhatsAppSimHubPlugin.Core
{
    public class WebSocketManager
    {
        private readonly string _pluginPath;
        private string _backendMode; // "whatsapp-web.js" or "baileys"
        private Process _nodeProcess;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isConnected = false;
        private int _selectedPort = 3000; // Dynamic port communicated by Node.js
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1); // Prevent concurrent starts
        private bool _isStarting = false; // Flag to prevent multiple starts

        public event EventHandler<string> QrCodeReceived;
        public event EventHandler<(string number, string name)> Ready;
        public event EventHandler<JObject> MessageReceived;
        public event EventHandler Disconnected;
        public event EventHandler<string> StatusChanged;

        // Google Contacts events
        public event EventHandler<(bool connected, string status)> GoogleStatusReceived;
        public event EventHandler<string> GoogleAuthUrlReceived;
        public event EventHandler GoogleContactsDone;  // Signal that contacts file was updated
        public event EventHandler<string> GoogleError;
        public event EventHandler<(string number, bool exists, string error)> CheckWhatsAppResult;

        // Public property to check connection
        public bool IsConnected => _isConnected && IsNodeProcessAlive;

        // Check if Node.js process is still alive
        public bool IsNodeProcessAlive
        {
            get
            {
                try
                {
                    return _nodeProcess != null && !_nodeProcess.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        // Properties for event subscription (compatibility)
        public event EventHandler<string> OnQrCode
        {
            add => QrCodeReceived += value;
            remove => QrCodeReceived -= value;
        }

        public event EventHandler<(string number, string name)> OnReady
        {
            add => Ready += value;
            remove => Ready -= value;
        }

        public event EventHandler<JObject> OnMessage
        {
            add => MessageReceived += value;
            remove => MessageReceived -= value;
        }

        public event EventHandler OnError
        {
            add => Disconnected += value;
            remove => Disconnected -= value;
        }

        public WebSocketManager(string pluginPath, string backendMode = "whatsapp-web.js")
        {
            _pluginPath = pluginPath;
            _backendMode = backendMode;
            EnsureNodeDirectories();
            EnsurePackageJson();
            EnsureServerScripts(); // Copy both scripts
        }

        /// <summary>
        /// Update backend mode (must reconnect after)
        /// </summary>
        public void SetBackendMode(string backendMode)
        {
            _backendMode = backendMode;
        }

        private void EnsureNodeDirectories()
        {
            var nodePath = Path.Combine(_pluginPath, "node");
            var dataPath = Path.Combine(_pluginPath, "data");
            var logsPath = Path.Combine(_pluginPath, "logs");

            Directory.CreateDirectory(nodePath);
            Directory.CreateDirectory(dataPath);
            Directory.CreateDirectory(logsPath);
        }

        private void EnsurePackageJson()
        {
            var packagePath = Path.Combine(_pluginPath, "node", "package.json");

            // Only create if it doesn't exist (user may have customized it)
            if (File.Exists(packagePath))
            {
                return;
            }

            // Copy from embedded resources
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "WhatsAppSimHubPlugin.Resources.package.json";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        File.WriteAllText(packagePath, reader.ReadToEnd());
                    }
                }
            }
        }

        private void EnsureServerScripts()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Copy whatsapp-server.js if not exists or version changed
            var whatsappScriptPath = Path.Combine(_pluginPath, "node", "whatsapp-server.js");
            CopyScriptIfNeeded(assembly, "WhatsAppSimHubPlugin.Resources.whatsapp-server.js", whatsappScriptPath);

            // Copy baileys-server.mjs if not exists or version changed
            var baileysScriptPath = Path.Combine(_pluginPath, "node", "baileys-server.mjs");
            CopyScriptIfNeeded(assembly, "WhatsAppSimHubPlugin.Resources.baileys-server.mjs", baileysScriptPath);

            // Copy google-contacts.js if not exists
            var googleContactsScriptPath = Path.Combine(_pluginPath, "node", "google-contacts.js");
            CopyScriptIfNeeded(assembly, "WhatsAppSimHubPlugin.Resources.google-contacts.js", googleContactsScriptPath);
        }

        /// <summary>
        /// Copy script from embedded resources only if it doesn't exist.
        /// Script updates are handled via UI "Check Updates" button which fetches from GitHub.
        /// </summary>
        private void CopyScriptIfNeeded(Assembly assembly, string resourceName, string targetPath)
        {
            try
            {
                // Only copy if file doesn't exist
                if (File.Exists(targetPath))
                {
                    return;
                }

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return;
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        File.WriteAllText(targetPath, reader.ReadToEnd());
                    }
                }
            }
            catch (Exception)
            {
                // Ignore script copy errors
            }
        }

        /// <summary>
        /// Starts Node.js and connects WebSocket.
        /// Dependencies must already be installed before calling this.
        /// </summary>
        public async Task StartAsync()
        {
            // Use semaphore to prevent concurrent starts
            if (!await _startLock.WaitAsync(0).ConfigureAwait(false))
            {
                StatusChanged?.Invoke(this, "Start already in progress - ignoring duplicate request");
                return;
            }

            try
            {
                if (_isConnected) return;
                if (_isStarting) return;

                // If there's already a Node process running, don't start another
                if (IsNodeProcessAlive)
                {
                    StatusChanged?.Invoke(this, "Node.js already running");
                    return;
                }

                _isStarting = true;

                await StartNodeProcess().ConfigureAwait(false);
                await ConnectWebSocket().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                throw;
            }
            finally
            {
                _isStarting = false;
                _startLock.Release();
            }
        }

        /// <summary>
        /// Kills old Node.js processes from the plugin (whatsapp-server.js or baileys-server.mjs)
        /// This fixes the EADDRINUSE problem when SimHub restarts!
        /// All operations run in background to not block SimHub
        /// </summary>
        private async Task KillOldNodeProcessesAsync()
        {
            // Run all logic in background thread to not block UI
            await Task.Run(async () =>
            {
                try
                {
                    var nodeProcesses = Process.GetProcessesByName("node");
                    int killedCount = 0;
                    var killTasks = new List<Task>();

                    foreach (var process in nodeProcesses)
                    {
                        try
                        {
                            string commandLine = GetProcessCommandLine(process);

                            // Check if it's a plugin script (whatsapp-server.js OR baileys-server.mjs)
                            if (!string.IsNullOrEmpty(commandLine) &&
                                (commandLine.Contains("whatsapp-server.js") || commandLine.Contains("baileys-server.mjs")))
                            {
                                // Don't kill the current process
                                if (_nodeProcess != null && process.Id == _nodeProcess.Id)
                                {
                                    continue;
                                }

                                process.Kill();
                                var procToWait = process;
                                killTasks.Add(Task.Run(() =>
                                {
                                    try { procToWait.WaitForExit(1000); } catch { }
                                }));
                                killedCount++;
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore errors when killing individual processes
                        }
                    }

                    if (killTasks.Count > 0)
                    {
                        await Task.WhenAll(killTasks).ConfigureAwait(false);
                    }

                    if (killedCount > 0)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // Ignore errors killing old processes
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the command line of a process (to check if it's whatsapp-server.js)
        /// NOTE: Uses WMI which can be slow - call only from background thread!
        /// </summary>
        private string GetProcessCommandLine(Process process)
        {
            try
            {
                // Use WMI to get command line
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        return obj["CommandLine"]?.ToString() ?? "";
                    }
                }
            }
            catch
            {
                // If WMI fails, try via MainModule
                try
                {
                    return process.MainModule?.FileName ?? "";
                }
                catch
                {
                    return "";
                }
            }

            return "";
        }

        private async Task StartNodeProcess()
        {
            await KillOldNodeProcessesAsync().ConfigureAwait(false);

            var scriptName = _backendMode == "baileys" ? "baileys-server.mjs" : "whatsapp-server.js";
            var scriptPath = Path.Combine(_pluginPath, "node", scriptName);

            if (!File.Exists(scriptPath))
            {
                var error = $"Script not found: {scriptPath}";
                StatusChanged?.Invoke(this, $"Error: {error}");
                throw new FileNotFoundException(error);
            }

            StatusChanged?.Invoke(this, "Starting");

            _nodeProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            // Handle process exit
            _nodeProcess.Exited += (sender, args) =>
            {
                _isConnected = false;
                StatusChanged?.Invoke(this, "Disconnected");
                Disconnected?.Invoke(this, EventArgs.Empty);
            };

            // Capture stderr
            _nodeProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    StatusChanged?.Invoke(this, $"NodeError: {e.Data}");
                }
            };

            _nodeProcess.Start();
            _nodeProcess.BeginErrorReadLine();

            _selectedPort = await Task.Run(() => ReadPortFromNodeSync()).ConfigureAwait(false);

            // Continue reading stdout in a separate Task (don't use BeginOutputReadLine)
            _ = Task.Run(() => ReadNodeOutputContinuously());

            await Task.Delay(2000).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the port from Node.js stdout synchronously (first line should be "PORT:XXXX")
        /// </summary>
        private int ReadPortFromNodeSync()
        {
            try
            {
                // Read first line (with implicit timeout - if process dies, ReadLine returns null)
                var portLine = _nodeProcess.StandardOutput.ReadLine();

                if (!string.IsNullOrEmpty(portLine) && portLine.StartsWith("PORT:"))
                {
                    var portStr = portLine.Substring(5); // Remove "PORT:"
                    if (int.TryParse(portStr, out int port) && port > 0 && port < 65536)
                    {
                        return port;
                    }
                }

                return 3000;
            }
            catch (Exception)
            {
                return 3000;
            }
        }

        /// <summary>
        /// Continues reading Node.js stdout in a separate Task
        /// </summary>
        private void ReadNodeOutputContinuously()
        {
            try
            {
                string line;
                while ((line = _nodeProcess?.StandardOutput?.ReadLine()) != null)
                {
                    // Silently consume output
                }
            }
            catch (Exception)
            {
                // Process terminated or stream closed - ignore
            }
        }

        private async Task ConnectWebSocket()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    _webSocket?.Dispose();
                    _cts?.Dispose();

                    _webSocket = new ClientWebSocket();
                    _cts = new CancellationTokenSource();

                    using (var connectCts = new CancellationTokenSource(10000))
                    {
                        await _webSocket.ConnectAsync(new Uri($"ws://127.0.0.1:{_selectedPort}"), connectCts.Token).ConfigureAwait(false);
                    }

                    _isConnected = true;
                    StatusChanged?.Invoke(this, "Connected");
                    _ = Task.Run(() => ReceiveMessages());

                    await SendCommandAsync("connect").ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    if (i == 2)
                    {
                        StatusChanged?.Invoke(this, "Error: Connection timed out after 3 attempts");
                        throw;
                    }
                    await Task.Delay(2000).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (i == 2)
                    {
                        StatusChanged?.Invoke(this, $"Error: Unable to connect - {ex.Message}");
                        throw;
                    }
                    await Task.Delay(2000).ConfigureAwait(false);
                }
            }
        }

        private async Task ReceiveMessages()
        {
            var buffer = new byte[8192];

            try
            {
                while (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            break;
                        }

                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var json = JObject.Parse(message);
                        var type = json["type"]?.ToString();

                        if (type == "qr")
                            QrCodeReceived?.Invoke(this, json["qr"].ToString());
                        else if (type == "ready")
                            Ready?.Invoke(this, (json["number"].ToString(), json["name"].ToString()));
                        else if (type == "message")
                            MessageReceived?.Invoke(this, json["message"] as JObject);
                        else if (type == "disconnected")
                            Disconnected?.Invoke(this, EventArgs.Empty);
                        // Google Contacts handlers
                        else if (type == "googleStatus")
                            GoogleStatusReceived?.Invoke(this, (json["connected"]?.ToObject<bool>() ?? false, json["status"]?.ToString() ?? "Unknown"));
                        else if (type == "googleAuthUrl")
                            GoogleAuthUrlReceived?.Invoke(this, json["url"]?.ToString());
                        else if (type == "googleAuthComplete")
                        {
                            bool success = json["success"]?.ToObject<bool>() ?? false;
                            if (success)
                                GoogleStatusReceived?.Invoke(this, (true, "Connected"));
                            else
                                GoogleError?.Invoke(this, json["error"]?.ToString() ?? "Authentication failed");
                        }
                        else if (type == "googleContactsDone")
                            GoogleContactsDone?.Invoke(this, EventArgs.Empty);
                        else if (type == "googleError")
                            GoogleError?.Invoke(this, json["error"]?.ToString() ?? "Unknown error");
                        else if (type == "checkWhatsAppResult")
                        {
                            var number = json["number"]?.ToString() ?? "";
                            var exists = json["exists"]?.ToObject<bool>() ?? false;
                            var error = json["error"]?.ToString();
                            CheckWhatsAppResult?.Invoke(this, (number, exists, error));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal cancellation, exit loop
                        break;
                    }
                    catch (WebSocketException)
                    {
                        // WebSocket error (connection lost), exit loop
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Catch any other exceptions but don't break the loop
                        // Log to file for debugging
                        try
                        {
                            var logPath = Path.Combine(_pluginPath, "logs", "websocket-errors.log");
                            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}\n{ex.StackTrace}\n\n");
                        }
                        catch { }
                        // Continue processing messages
                    }
                }
            }
            finally
            {
                // Connection lost - update state and notify
                _isConnected = false;
                StatusChanged?.Invoke(this, "Disconnected");
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task SendReplyAsync(string chatId, string text)
        {
            if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            {
                return;
            }

            var json = new JObject
            {
                ["type"] = "sendReply",
                ["chatId"] = chatId,
                ["text"] = text
            };

            var bytes = Encoding.UTF8.GetBytes(json.ToString());
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        public async Task SendCommandAsync(string commandType)
        {
            var json = new JObject
            {
                ["type"] = commandType
            };

            var bytes = Encoding.UTF8.GetBytes(json.ToString());
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        public async Task SendJsonAsync(object data)
        {
            var json = JObject.FromObject(data);
            var bytes = Encoding.UTF8.GetBytes(json.ToString());
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        public void Stop()
        {
            _isConnected = false;
            _isStarting = false;

            // Close WebSocket first
            try
            {
                _cts?.Cancel();
                _webSocket?.Dispose();
                _webSocket = null;
            }
            catch { }

            // Kill Node.js process and ALL its child processes (including Chrome/Puppeteer)
            var processToCleanup = _nodeProcess;
            _nodeProcess = null;

            if (processToCleanup != null)
            {
                try
                {
                    if (!processToCleanup.HasExited)
                    {
                        int nodePid = processToCleanup.Id;

                        // Kill entire process tree (Node + Chrome + any children)
                        KillProcessTree(nodePid);
                    }
                }
                catch { }
                finally
                {
                    try { processToCleanup.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// Kill a process and all its child processes recursively using taskkill /T
        /// </summary>
        private void KillProcessTree(int pid)
        {
            try
            {
                // Use taskkill with /T flag to kill entire process tree
                var killProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {pid} /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                killProcess.Start();
                killProcess.WaitForExit(5000); // Wait up to 5 seconds
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
