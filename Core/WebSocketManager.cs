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

        // Propriedade pública para verificar conexão
        public bool IsConnected => _isConnected;

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

            // ✅ REVERTIDO: Versão que FUNCIONAVA!
            // Se mudar versão, apagar pasta node_modules para forçar reinstalação:
            // %AppData%\SimHub\WhatsAppPlugin\node\node_modules\

            var packageJson = @"{
  ""name"": ""whatsapp-plugin"",
  ""version"": ""1.0.0"",
  ""dependencies"": {
    ""whatsapp-web.js"": ""github:DouglasReisofc/douglasreiswebjs"",
    ""ws"": ""^8.14.2"",
    ""puppeteer"": ""^21.0.0""
  }
}";

            File.WriteAllText(packagePath, packageJson);
        }

        private void EnsureServerScripts()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Copiar whatsapp-server.js
            var whatsappScriptPath = Path.Combine(_pluginPath, "node", "whatsapp-server.js");
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

            // Copiar baileys-server.mjs
            var baileysScriptPath = Path.Combine(_pluginPath, "node", "baileys-server.mjs");
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

            // Copiar package.json (com ambas as bibliotecas: whatsapp-web.js e Baileys)
            var packageJsonPath = Path.Combine(_pluginPath, "node", "package.json");
            var packageJsonResourceName = "WhatsAppSimHubPlugin.Resources.package.json";

            using (Stream stream = assembly.GetManifestResourceStream(packageJsonResourceName))
            {
                if (stream != null)
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        File.WriteAllText(packageJsonPath, reader.ReadToEnd());
                    }
                }
            }
        }

        public async Task StartAsync()
        {
            if (_isConnected) return;

            try
            {
                await EnsureNpmPackagesInstalled();
                await StartNodeProcess();
                await ConnectWebSocket();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                throw; // Re-throw para o chamador saber que falhou
            }
        }

        private async Task EnsureNpmPackagesInstalled()
        {
            var nodeModulesPath = Path.Combine(_pluginPath, "node", "node_modules");

            StatusChanged?.Invoke(this, "Debug: Checking npm packages");

            if (Directory.Exists(nodeModulesPath))
            {
                var whatsappPath = Path.Combine(nodeModulesPath, "whatsapp-web.js");
                if (Directory.Exists(whatsappPath))
                {
                    StatusChanged?.Invoke(this, "Debug: Packages already installed");
                    return;
                }
            }

            // ⭐ SÓ NOTIFICAR "Installing" SE REALMENTE VAI INSTALAR!
            StatusChanged?.Invoke(this, "Installing");

            await Task.Run(async () =>
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
                    }
                };

                StatusChanged?.Invoke(this, "Debug: Starting npm install");
                process.Start();

                // Ler output
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit(120000);

                StatusChanged?.Invoke(this, $"Debug: npm exit code = {process.ExitCode}");

                if (!string.IsNullOrEmpty(output))
                    StatusChanged?.Invoke(this, $"Debug: npm output = {output.Substring(0, Math.Min(200, output.Length))}");

                if (!string.IsNullOrEmpty(error))
                    StatusChanged?.Invoke(this, $"Debug: npm error = {error.Substring(0, Math.Min(200, error.Length))}");

                // ⭐ VERIFICAR SE FALHOU POR FALTA DE GIT
                if (process.ExitCode != 0 || !string.IsNullOrEmpty(error))
                {
                    // Erro comum: Git não instalado
                    if (error.Contains("spawn git") || error.Contains("ENOENT") || process.ExitCode == -4058)
                    {
                        StatusChanged?.Invoke(this, "⚠️ Git not found - Installing automatically...");

                        // ⭐ INSTALAR GIT AUTOMATICAMENTE!
                        bool gitInstalled = await InstallGitSilently();

                        if (gitInstalled)
                        {
                            StatusChanged?.Invoke(this, "✅ Git installed successfully!");
                            StatusChanged?.Invoke(this, "🔄 Retrying npm install...");

                            // ⭐ TENTAR NPM INSTALL NOVAMENTE (SEM Task.Run, já estamos em background!)
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
                                }
                            };

                            retryProcess.Start();
                            retryProcess.WaitForExit(120000);

                            if (retryProcess.ExitCode == 0)
                            {
                                StatusChanged?.Invoke(this, "✅ npm install completed successfully!");
                            }
                        }
                        else
                        {
                            StatusChanged?.Invoke(this, "❌ Could not install Git automatically");
                            StatusChanged?.Invoke(this, "📥 Please install manually: https://git-scm.com/download/win");
                            StatusChanged?.Invoke(this, "Error: Git required");
                            return;
                        }
                    }
                    else
                    {
                        // Outro erro
                        StatusChanged?.Invoke(this, $"❌ ERROR: npm install failed (exit code: {process.ExitCode})");
                        StatusChanged?.Invoke(this, "Error: npm install failed");
                        return;
                    }
                }
            });

            // ⭐ NOTIFICAR QUE TERMINOU COM SUCESSO
            StatusChanged?.Invoke(this, "Installed");
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
        /// </summary>
        private void KillOldNodeProcesses()
        {
            try
            {
                StatusChanged?.Invoke(this, "Debug: Killing old Node.js processes...");

                // Procurar todos os processos node.exe
                var nodeProcesses = Process.GetProcessesByName("node");

                int killedCount = 0;

                foreach (var process in nodeProcesses)
                {
                    try
                    {
                        // Verificar se é o nosso processo (whatsapp-server.js)
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
                            process.WaitForExit(1000); // Esperar max 1s
                            killedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Ignorar erros ao matar processos individuais
                        StatusChanged?.Invoke(this, $"Debug: Could not kill process: {ex.Message}");
                    }
                }

                if (killedCount > 0)
                {
                    StatusChanged?.Invoke(this, $"Debug: Killed {killedCount} old Node.js process(es)");

                    // Dar tempo para o SO libertar a porta
                    System.Threading.Thread.Sleep(500);
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
        }

        /// <summary>
        /// Obtem a linha de comando de um processo (para verificar se é whatsapp-server.js)
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

        private Task StartNodeProcess()
        {
            StatusChanged?.Invoke(this, "Debug: StartNodeProcess called");

            // 🔥 MATAR PROCESSOS NODE.JS ANTIGOS!
            KillOldNodeProcesses();

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
            return Task.Delay(3000); // ⭐ 3s para Node.js iniciar
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
                    await _webSocket.ConnectAsync(new Uri("ws://127.0.0.1:3000"), _cts.Token);
                    _isConnected = true;
                    StatusChanged?.Invoke(this, "Connected");
                    _ = Task.Run(() => ReceiveMessages());

                    // Enviar comando "connect" para Baileys iniciar conexão
                    await SendCommandAsync("connect");
                    return;
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Debug: Attempt {i + 1} failed: {ex.Message}");

                    if (i == 2) // última tentativa
                    {
                        StatusChanged?.Invoke(this, $"Error: Unable to connect - {ex.Message}");
                        throw;
                    }
                    await Task.Delay(2000); // esperar 2s entre tentativas
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
            _isConnected = false;
            _cts?.Cancel();
            _webSocket?.Dispose();

            // ⭐ Try-catch para evitar "No process is associated"
            try
            {
                if (_nodeProcess != null && !_nodeProcess.HasExited)
                {
                    _nodeProcess.Kill();
                    _nodeProcess.Dispose();
                    _nodeProcess = null;
                }
            }
            catch (InvalidOperationException)
            {
                // Processo já não existe - OK!
                _nodeProcess = null;
            }
            catch (Exception)
            {
                // Qualquer outro erro - limpar referência
                _nodeProcess = null;
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

                installProcess.Start();

                // Aguardar até 3 minutos (instalação pode demorar)
                bool finished = installProcess.WaitForExit(180000);

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
