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

        public event EventHandler<string> QrCodeReceived;
        public event EventHandler<(string number, string name)> Ready;
        public event EventHandler<JObject> MessageReceived;
        public event EventHandler Disconnected;
        public event EventHandler<string> StatusChanged;  // ← NOVO EVENTO!
        public event EventHandler<JArray> ChatContactsListReceived;  // 📱 Contactos das conversas
        public event EventHandler<string> ChatContactsError;  // ❌ Erro ao carregar
        public event EventHandler<bool> InstallationCompleted;  // 🔧 Instalação terminou (true=sucesso)

        // Propriedade pública para verificar conexão
        public bool IsConnected => _isConnected;

        // Flag para tracking de instalação em progresso
        private bool _isInstalling = false;
        public bool IsInstalling => _isInstalling;

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

            // Copy whatsapp-server.js only if it doesn't exist
            var whatsappScriptPath = Path.Combine(_pluginPath, "node", "whatsapp-server.js");
            if (!File.Exists(whatsappScriptPath))
            {
                var whatsappResourceName = "WhatsAppSimHubPlugin.Resources.whatsapp-server.js";
                using (Stream stream = assembly.GetManifestResourceStream(whatsappResourceName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            File.WriteAllText(whatsappScriptPath, reader.ReadToEnd());
                        }
                    }
                }
            }

            // Copy baileys-server.mjs only if it doesn't exist
            var baileysScriptPath = Path.Combine(_pluginPath, "node", "baileys-server.mjs");
            if (!File.Exists(baileysScriptPath))
            {
                var baileysResourceName = "WhatsAppSimHubPlugin.Resources.baileys-server.mjs";
                using (Stream stream = assembly.GetManifestResourceStream(baileysResourceName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            File.WriteAllText(baileysScriptPath, reader.ReadToEnd());
                        }
                    }
                }
            }
        }

        public async Task StartAsync()
        {
            if (_isConnected) return;
            if (_isInstalling)
            {
                StatusChanged?.Invoke(this, "Debug: Installation already in progress, skipping StartAsync");
                return;
            }

            try
            {
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

                StatusChanged?.Invoke(this, "Debug: Checking npm packages");

                // Verificar se package.json mudou desde último install
                if (File.Exists(packageJsonPath) && File.Exists(packageLockPath))
                {
                    var packageJsonTime = File.GetLastWriteTime(packageJsonPath);
                    var packageLockTime = File.GetLastWriteTime(packageLockPath);

                    if (packageJsonTime > packageLockTime)
                    {
                        StatusChanged?.Invoke(this, "Debug: package.json changed - reinstalling needed");
                        return true;
                    }
                }

                if (Directory.Exists(nodeModulesPath))
                {
                    var whatsappPath = Path.Combine(nodeModulesPath, "whatsapp-web.js");
                    if (Directory.Exists(whatsappPath))
                    {
                        StatusChanged?.Invoke(this, "Debug: Packages already installed");
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
            StatusChanged?.Invoke(this, "Debug: Finding npm");
            var npm = FindNpm();
            StatusChanged?.Invoke(this, $"Debug: npm path = {npm}");

            var workDir = Path.Combine(_pluginPath, "node");
            StatusChanged?.Invoke(this, $"Debug: work dir = {workDir}");

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
                    StatusChanged?.Invoke(this, $"npm error: {e.Data}");
                }
            };

            StatusChanged?.Invoke(this, "Debug: Starting npm install");
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
            StatusChanged?.Invoke(this, $"Debug: npm exit code = {process.ExitCode}");

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

            retryProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    StatusChanged?.Invoke(this, $"npm: {e.Data}");
            };

            retryProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    StatusChanged?.Invoke(this, $"npm error: {e.Data}");
            };

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
        /// 🔥 Mata processos Node.js antigos que estão a usar whatsapp-server.js
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
                    StatusChanged?.Invoke(this, "Debug: Killing old Node.js processes...");

                    // Procurar todos os processos node.exe (operação potencialmente lenta)
                    var nodeProcesses = Process.GetProcessesByName("node");

                    int killedCount = 0;
                    var killTasks = new List<Task>();

                    foreach (var process in nodeProcesses)
                    {
                        try
                        {
                            // Verificar se é o nosso processo (whatsapp-server.js)
                            // GetProcessCommandLine usa WMI que pode ser lento, mas estamos em background
                            string commandLine = GetProcessCommandLine(process);

                            if (!string.IsNullOrEmpty(commandLine) &&
                                commandLine.Contains("whatsapp-server.js"))
                            {
                                // Skip se for o processo actual
                                if (_nodeProcess != null && process.Id == _nodeProcess.Id)
                                {
                                    continue;
                                }

                                StatusChanged?.Invoke(this, $"Debug: Killing Node.js process {process.Id}");

                                process.Kill();
                                // Aguardar processo terminar
                                var procToWait = process;
                                killTasks.Add(Task.Run(() =>
                                {
                                    try
                                    {
                                        procToWait.WaitForExit(1000);
                                    }
                                    catch { }
                                }));
                                killedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignorar erros ao matar processos individuais
                            StatusChanged?.Invoke(this, $"Debug: Could not kill process: {ex.Message}");
                        }
                    }

                    // Aguardar todos os kills completarem
                    if (killTasks.Count > 0)
                    {
                        await Task.WhenAll(killTasks).ConfigureAwait(false);
                    }

                    if (killedCount > 0)
                    {
                        StatusChanged?.Invoke(this, $"Debug: Killed {killedCount} old Node.js process(es)");

                        // Dar tempo para o SO libertar a porta
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "Debug: No old Node.js processes found");
                    }
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Debug: Error killing old processes: {ex.Message}");
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
            StatusChanged?.Invoke(this, "Debug: StartNodeProcess called");

            // 🔥 MATAR PROCESSOS NODE.JS ANTIGOS!
            await KillOldNodeProcessesAsync().ConfigureAwait(false);

            // Escolher script baseado no backend mode
            var scriptName = _backendMode == "baileys" ? "baileys-server.mjs" : "whatsapp-server.js";
            var scriptPath = Path.Combine(_pluginPath, "node", scriptName);
            StatusChanged?.Invoke(this, $"Debug: Starting {_backendMode} backend");
            StatusChanged?.Invoke(this, $"Debug: script path = {scriptPath}");

            // ⭐ VERIFICAR SE SCRIPT EXISTE
            if (!File.Exists(scriptPath))
            {
                var error = $"Script not found: {scriptPath}";
                StatusChanged?.Invoke(this, $"Error: {error}");
                throw new FileNotFoundException(error);
            }

            StatusChanged?.Invoke(this, "Debug: Script file exists");

            // ⭐ NOTIFICAR QUE ESTÁ A INICIAR NODE
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
                }
            };

            // ⭐ CAPTURAR STDERR PARA DEBUG
            _nodeProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[NODE ERROR] {e.Data}");
                    StatusChanged?.Invoke(this, $"NodeError: {e.Data}");
                }
            };

            // ⭐ CAPTURAR STDOUT TAMBÉM
            _nodeProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[NODE OUTPUT] {e.Data}");
                    StatusChanged?.Invoke(this, $"NodeOutput: {e.Data}");
                }
            };

            StatusChanged?.Invoke(this, "Debug: Starting node process");
            _nodeProcess.Start();
            _nodeProcess.BeginErrorReadLine();
            _nodeProcess.BeginOutputReadLine();

            StatusChanged?.Invoke(this, "Debug: Node process started, waiting 3s");
            await Task.Delay(3000).ConfigureAwait(false); // ⭐ 3s para Node.js iniciar
        }

        private async Task ConnectWebSocket()
        {
            StatusChanged?.Invoke(this, "Debug: ConnectWebSocket called");

            // ⭐ RETRY 3x COM DELAY
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    // ⭐ CRIAR NOVO WebSocket EM CADA TENTATIVA!
                    _webSocket?.Dispose();
                    _cts?.Dispose();

                    _webSocket = new ClientWebSocket();
                    _cts = new CancellationTokenSource();

                    StatusChanged?.Invoke(this, $"Debug: Connecting attempt {i + 1}/3");

                    // Timeout de 10 segundos para conexão WebSocket
                    using (var connectCts = new CancellationTokenSource(10000))
                    {
                        await _webSocket.ConnectAsync(new Uri("ws://127.0.0.1:3000"), connectCts.Token).ConfigureAwait(false);
                    }

                    _isConnected = true;
                    StatusChanged?.Invoke(this, "Connected");
                    _ = Task.Run(() => ReceiveMessages());

                    // Enviar comando "connect" para Baileys iniciar conexão
                    await SendCommandAsync("connect").ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    StatusChanged?.Invoke(this, $"Debug: Attempt {i + 1} timed out");

                    if (i == 2)
                    {
                        StatusChanged?.Invoke(this, "Error: Connection timed out after 3 attempts");
                        throw;
                    }
                    await Task.Delay(2000).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Debug: Attempt {i + 1} failed: {ex.Message}");

                    if (i == 2) // última tentativa
                    {
                        StatusChanged?.Invoke(this, $"Error: Unable to connect - {ex.Message}");
                        throw;
                    }
                    await Task.Delay(2000).ConfigureAwait(false); // esperar 2s entre tentativas
                }
            }
        }

        private async Task ReceiveMessages()
        {
            var buffer = new byte[8192];

            while (_webSocket.State == WebSocketState.Open)
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
                catch { }
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
            StatusChanged?.Invoke(this, "Debug: Stop() called.");
            _isConnected = false;

            try
            {
                // 1. Try to send graceful shutdown command (fire-and-forget para não bloquear SimHub)
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    StatusChanged?.Invoke(this, "Debug: Sending 'shutdown' command to Node.js script.");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendCommandAsync("shutdown").ConfigureAwait(false);
                        }
                        catch { /* Ignore shutdown errors */ }
                    });
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Debug: Error sending shutdown command: {ex.Message}");
            }

            // 2. Cancel any ongoing WebSocket operations
            try
            {
                _cts?.Cancel();
                _webSocket?.Dispose();
            }
            catch { }


            // 3. Wait for the process to exit gracefully, with a fallback to Kill (async para não bloquear SimHub)
            var processToCleanup = _nodeProcess;
            _nodeProcess = null;

            if (processToCleanup != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!processToCleanup.HasExited)
                        {
                            StatusChanged?.Invoke(this, "Debug: Waiting for Node.js process to exit...");

                            // Aguardar até 5 segundos de forma async
                            var exitTask = Task.Run(() => processToCleanup.WaitForExit(5000));
                            bool exited = await exitTask.ConfigureAwait(false);

                            if (exited)
                            {
                                StatusChanged?.Invoke(this, "Debug: Node.js process exited gracefully.");
                            }
                            else
                            {
                                StatusChanged?.Invoke(this, "Debug: Node.js process did not exit in time. Killing it.");
                                try { processToCleanup.Kill(); } catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke(this, $"Debug: Exception during process stop: {ex.Message}");
                        try { if (!processToCleanup.HasExited) processToCleanup.Kill(); } catch { }
                    }
                    finally
                    {
                        try { processToCleanup.Dispose(); } catch { }
                        StatusChanged?.Invoke(this, "Debug: Stop() finished.");
                    }
                });
            }
            else
            {
                StatusChanged?.Invoke(this, "Debug: Stop() finished.");
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
