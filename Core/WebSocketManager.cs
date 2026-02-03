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
        private string _backendMode; // "whatsapp-web.js" ou "baileys"
        private Process _nodeProcess;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isConnected = false;
        private int _selectedPort = 3000; // Porta dinâmica comunicada pelo Node.js
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1); // Prevent concurrent starts
        private bool _isStarting = false; // Flag to prevent multiple starts

        public event EventHandler<string> QrCodeReceived;
        public event EventHandler<(string number, string name)> Ready;
        public event EventHandler<JObject> MessageReceived;
        public event EventHandler Disconnected;
        public event EventHandler<string> StatusChanged;  // ← NOVO EVENTO!
        public event EventHandler<JArray> ChatContactsListReceived;  // 📱 Contactos das conversas
        public event EventHandler<string> ChatContactsError;  // ❌ Erro ao carregar
        public event EventHandler<bool> InstallationCompleted;  // 🔧 Instalação terminou (true=sucesso)

        // Propriedade pública para verificar conexão
        public bool IsConnected => _isConnected && IsNodeProcessAlive;

        // Flag para tracking de instalação em progresso
        private bool _isInstalling = false;
        public bool IsInstalling => _isInstalling;

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

        // Propriedades para subscrição de eventos (compatibilidade)
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
            EnsureServerScripts(); // Copiar ambos os scripts
        }

        /// <summary>
        /// Atualizar backend mode (deve reconectar depois)
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
                if (_isInstalling) return;
                if (_isStarting) return;

                // Se já há um processo Node a correr, não iniciar outro
                if (IsNodeProcessAlive)
                {
                    StatusChanged?.Invoke(this, "Node.js already running");
                    return;
                }

                _isStarting = true;

                // Verificar se precisa instalar pacotes
                bool needsInstall = await CheckIfNpmInstallNeeded().ConfigureAwait(false);

                if (needsInstall)
                {
                    // Iniciar instalação em background (event-based, não bloqueia)
                    StartNpmInstallBackground();
                    // O fluxo continua quando InstallationCompleted for disparado
                    return;
                }

                // Pacotes já instalados, continuar normalmente
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
        /// Verifica se npm install é necessário (corre em background para não bloquear)
        /// </summary>
        private Task<bool> CheckIfNpmInstallNeeded()
        {
            // Executar verificações de I/O em background thread
            return Task.Run(() =>
            {
                var nodeModulesPath = Path.Combine(_pluginPath, "node", "node_modules");
                var packageJsonPath = Path.Combine(_pluginPath, "node", "package.json");
                var packageLockPath = Path.Combine(_pluginPath, "node", "package-lock.json");

                // Verificar se package.json mudou desde último install
                if (File.Exists(packageJsonPath) && File.Exists(packageLockPath))
                {
                    var packageJsonTime = File.GetLastWriteTime(packageJsonPath);
                    var packageLockTime = File.GetLastWriteTime(packageLockPath);

                    if (packageJsonTime > packageLockTime)
                    {
                        return true;
                    }
                }

                if (Directory.Exists(nodeModulesPath))
                {
                    var whatsappPath = Path.Combine(nodeModulesPath, "whatsapp-web.js");
                    if (Directory.Exists(whatsappPath))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        /// <summary>
        /// Inicia npm install em background (event-based, não bloqueia SimHub)
        /// Dispara InstallationCompleted quando terminar
        /// </summary>
        private void StartNpmInstallBackground()
        {
            if (_isInstalling) return;

            _isInstalling = true;
            StatusChanged?.Invoke(this, "Installing");

            // Fire-and-forget: corre em background thread
            _ = Task.Run(async () =>
            {
                bool success = false;
                try
                {
                    success = await RunNpmInstallAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Error: npm install exception: {ex.Message}");
                    success = false;
                }
                finally
                {
                    _isInstalling = false;

                    // Disparar evento de conclusão
                    InstallationCompleted?.Invoke(this, success);

                    if (success)
                    {
                        StatusChanged?.Invoke(this, "✅ Installation completed - continuing startup...");

                        // Continuar o fluxo normal após instalação
                        try
                        {
                            await StartNodeProcess().ConfigureAwait(false);
                            await ConnectWebSocket().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Executa npm install (corre em background thread)
        /// </summary>
        private async Task<bool> RunNpmInstallAsync()
        {
            var npm = FindNpm();
            var workDir = Path.Combine(_pluginPath, "node");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = npm,
                    Arguments = "install",
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    StatusChanged?.Invoke(this, $"npm: {e.Data}");
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Aguardar processo terminar (em background, não bloqueia UI)
            var exitedTask = new TaskCompletionSource<bool>();
            process.Exited += (s, e) => exitedTask.TrySetResult(true);

            // Timeout de 2 minutos
            var timeoutTask = Task.Delay(120000);
            var completedTask = await Task.WhenAny(exitedTask.Task, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                StatusChanged?.Invoke(this, "❌ npm install timeout (2 minutes)");
                try { process.Kill(); } catch { }
                return false;
            }

            var error = errorBuilder.ToString();

            // Verificar se falhou por falta de Git
            if (process.ExitCode != 0 || error.Contains("spawn git") || error.Contains("ENOENT"))
            {
                if (error.Contains("spawn git") || error.Contains("ENOENT") || process.ExitCode == -4058)
                {
                    StatusChanged?.Invoke(this, "⚠️ Git not found - Installing automatically...");

                    bool gitInstalled = await InstallGitSilently().ConfigureAwait(false);

                    if (gitInstalled)
                    {
                        StatusChanged?.Invoke(this, "✅ Git installed successfully!");
                        StatusChanged?.Invoke(this, "🔄 Retrying npm install...");

                        // Retry npm install
                        return await RetryNpmInstallAsync(npm, workDir).ConfigureAwait(false);
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "❌ Could not install Git automatically");
                        StatusChanged?.Invoke(this, "📥 Please install manually: https://git-scm.com/download/win");
                        StatusChanged?.Invoke(this, "Error: Git required");
                        return false;
                    }
                }
                else
                {
                    StatusChanged?.Invoke(this, $"❌ npm install failed (exit code: {process.ExitCode})");
                    return false;
                }
            }

            StatusChanged?.Invoke(this, "✅ npm install completed successfully!");
            return true;
        }

        /// <summary>
        /// Retry npm install após instalar Git
        /// </summary>
        private async Task<bool> RetryNpmInstallAsync(string npm, string workDir)
        {
            var retryProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = npm,
                    Arguments = "install",
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            retryProcess.OutputDataReceived += (s, e) => { };
            retryProcess.ErrorDataReceived += (s, e) => { };

            retryProcess.Start();
            retryProcess.BeginOutputReadLine();
            retryProcess.BeginErrorReadLine();

            var exitedTask = new TaskCompletionSource<bool>();
            retryProcess.Exited += (s, e) => exitedTask.TrySetResult(true);

            var timeoutTask = Task.Delay(120000);
            var completedTask = await Task.WhenAny(exitedTask.Task, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                StatusChanged?.Invoke(this, "❌ npm install retry timeout");
                try { retryProcess.Kill(); } catch { }
                return false;
            }

            if (retryProcess.ExitCode == 0)
            {
                StatusChanged?.Invoke(this, "✅ npm install completed successfully!");
                return true;
            }

            StatusChanged?.Invoke(this, $"❌ npm install retry failed (exit code: {retryProcess.ExitCode})");
            return false;
        }

        private string FindNpm()
        {
            var paths = new[]
            {
                @"C:\Program Files\nodejs\npm.cmd",
                @"C:\Program Files (x86)\nodejs\npm.cmd"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "npm.cmd";
        }

        /// <summary>
        /// 🔥 Mata processos Node.js antigos do plugin (whatsapp-server.js ou baileys-server.mjs)
        /// Isto resolve o problema de EADDRINUSE quando SimHub reinicia!
        /// Toda a operação corre em background para não bloquear SimHub
        /// </summary>
        private async Task KillOldNodeProcessesAsync()
        {
            // Executar toda a lógica em background thread para não bloquear UI
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

                            // Verificar se é script do plugin (whatsapp-server.js OU baileys-server.mjs)
                            if (!string.IsNullOrEmpty(commandLine) &&
                                (commandLine.Contains("whatsapp-server.js") || commandLine.Contains("baileys-server.mjs")))
                            {
                                // Não matar o processo atual
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
                            // Ignorar erros ao matar processos individuais
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
        /// Obtem a linha de comando de um processo (para verificar se é whatsapp-server.js)
        /// NOTA: Usa WMI que pode ser lento - chamar apenas de background thread!
        /// </summary>
        private string GetProcessCommandLine(Process process)
        {
            try
            {
                // Usar WMI para obter command line
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
                // Se WMI falhar, tentar pelo MainModule
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

            // Continuar a ler stdout numa Task separada (não usar BeginOutputReadLine)
            _ = Task.Run(() => ReadNodeOutputContinuously());

            await Task.Delay(2000).ConfigureAwait(false);
        }

        /// <summary>
        /// Lê a porta do stdout do Node.js de forma síncrona (primeira linha deve ser "PORT:XXXX")
        /// </summary>
        private int ReadPortFromNodeSync()
        {
            try
            {
                // Ler primeira linha (com timeout implícito - se o processo morrer, ReadLine retorna null)
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
        /// Continua a ler stdout do Node.js numa Task separada
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
                // Processo terminou ou stream fechado - ignorar
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
                        else if (type == "chatContactsList")
                            ChatContactsListReceived?.Invoke(this, json["contacts"] as JArray);
                        else if (type == "chatContactsError")
                            ChatContactsError?.Invoke(this, json["error"]?.ToString() ?? "Unknown error");
                        else if (type == "disconnected")
                            Disconnected?.Invoke(this, EventArgs.Empty);
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

        public void Stop()
        {
            _isConnected = false;
            _isStarting = false;

            // Enviar shutdown ao Node.js de forma síncrona (com timeout curto)
            try
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    var shutdownJson = new JObject { ["type"] = "shutdown" };
                    var bytes = Encoding.UTF8.GetBytes(shutdownJson.ToString());

                    using (var cts = new CancellationTokenSource(1000)) // 1 segundo timeout
                    {
                        try
                        {
                            _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token).Wait();
                        }
                        catch { }
                    }
                }
            }
            catch (Exception)
            {
            }

            // Fechar WebSocket
            try
            {
                _cts?.Cancel();
                _webSocket?.Dispose();
                _webSocket = null;
            }
            catch { }

            // Matar processo Node.js imediatamente
            var processToCleanup = _nodeProcess;
            _nodeProcess = null;

            if (processToCleanup != null)
            {
                try
                {
                    if (!processToCleanup.HasExited)
                    {
                        processToCleanup.Kill();
                        processToCleanup.WaitForExit(2000);
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
        /// Instala Git silenciosamente (sem UI) se não estiver instalado
        /// </summary>
        private async Task<bool> InstallGitSilently()
        {
            try
            {
                StatusChanged?.Invoke(this, "📥 Downloading Git installer...");

                // URL do instalador Git (64-bit)
                string gitUrl = "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.1/Git-2.47.1-64-bit.exe";
                string tempPath = Path.Combine(Path.GetTempPath(), "GitInstaller.exe");

                // Download do instalador
                using (var client = new System.Net.WebClient())
                {
                    await client.DownloadFileTaskAsync(gitUrl, tempPath);
                }

                StatusChanged?.Invoke(this, "⚙️ Installing Git silently (this may take 1-2 minutes)...");

                // Instalar silenciosamente
                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = tempPath,
                        // Flags para instalação completamente silenciosa:
                        // /VERYSILENT - Sem UI
                        // /NORESTART - Não reiniciar
                        // /SUPPRESSMSGBOXES - Sem popup messages
                        // /SP- - Não mostrar "preparing to install"
                        Arguments = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /SP-",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                installProcess.EnableRaisingEvents = true;
                installProcess.Start();

                // Aguardar até 3 minutos (instalação pode demorar) - event-based
                var exitedTask = new TaskCompletionSource<bool>();
                installProcess.Exited += (s, e) => exitedTask.TrySetResult(true);

                var timeoutTask = Task.Delay(180000);
                var completedTask = await Task.WhenAny(exitedTask.Task, timeoutTask).ConfigureAwait(false);

                bool finished = completedTask != timeoutTask;

                if (finished && installProcess.ExitCode == 0)
                {
                    // Limpar instalador temporário
                    try { File.Delete(tempPath); } catch { }

                    // ⭐ ATUALIZAR PATH ENVIRONMENT VARIABLE
                    // Git instala em C:\Program Files\Git\cmd por default
                    string gitPath = @"C:\Program Files\Git\cmd";
                    if (Directory.Exists(gitPath))
                    {
                        // Adicionar ao PATH da sessão atual
                        string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                        if (!currentPath.Contains(gitPath))
                        {
                            Environment.SetEnvironmentVariable("PATH",
                                currentPath + ";" + gitPath,
                                EnvironmentVariableTarget.Process);
                        }
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ Git installation failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
