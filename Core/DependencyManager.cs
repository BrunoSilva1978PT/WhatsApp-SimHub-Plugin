using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Manages verification and installation of dependencies (Node.js, Git, npm packages)
    /// </summary>
    public class DependencyManager
    {
        public event EventHandler<string> StatusChanged;

        private readonly string _pluginPath;

        public DependencyManager(string pluginPath)
        {
            _pluginPath = pluginPath;
        }

        #region Node.js

        /// <summary>
        /// Checks if Node.js is installed (portable or global)
        /// </summary>
        public bool IsNodeInstalled()
        {
            try
            {
                Log("Checking Node.js installation...");

                // 1. CHECK PORTABLE VERSION (priority!)
                string nodePortablePath = Path.Combine(_pluginPath, "tools", "node");
                Log($"Checking portable path: {nodePortablePath}");

                if (Directory.Exists(nodePortablePath))
                {
                    Log("Portable node folder exists!");
                    var subdirs = Directory.GetDirectories(nodePortablePath);
                    Log($"Found {subdirs.Length} subdirectories");

                    if (subdirs.Length > 0)
                    {
                        string nodeExe = Path.Combine(subdirs[0], "node.exe");
                        Log($"Checking for node.exe at: {nodeExe}");

                        if (File.Exists(nodeExe))
                        {
                            Log($"✅ Node.exe found!");

                            // Add to PATH
                            string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                            if (!currentPath.Contains(subdirs[0]))
                            {
                                Environment.SetEnvironmentVariable("PATH",
                                    subdirs[0] + ";" + currentPath,
                                    EnvironmentVariableTarget.Process);
                                Log($"Added to PATH: {subdirs[0]}");
                            }

                            Log($"Node.js portable found at: {subdirs[0]}");
                            return true;
                        }
                        else
                        {
                            Log($"❌ Node.exe NOT found at: {nodeExe}");
                        }
                    }
                    else
                    {
                        Log("❌ No subdirectories in portable node folder");
                    }
                }
                else
                {
                    Log($"❌ Portable node folder does not exist: {nodePortablePath}");
                }

                // 2. CHECK GLOBAL INSTALLATION
                Log("Checking global Node.js installations...");
                var paths = new[]
                {
                    @"C:\Program Files\nodejs\node.exe",
                    @"C:\Program Files (x86)\nodejs\node.exe"
                };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        Log($"Node.js found at: {path}");
                        return true;
                    }
                }

                // 3. TRY VIA PATH
                Log("Trying to run node --version...");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    Log($"Node.js version: {output.Trim()}");
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Installs Node.js PORTABLE (no UAC, no admin, no popup!)
        /// </summary>
        public async Task<bool> InstallNodeSilently()
        {
            try
            {
                Log("Downloading Node.js portable...");

                // Local folder for Node.js portable
                string nodePortablePath = Path.Combine(_pluginPath, "tools", "node");
                Directory.CreateDirectory(nodePortablePath);

                // Node.js PORTABLE URL (zip)
                string nodeUrl = "https://nodejs.org/dist/v20.11.0/node-v20.11.0-win-x64.zip";
                string zipPath = Path.Combine(Path.GetTempPath(), "node-portable.zip");

                // Download zip
                using (var client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(nodeUrl, zipPath).ConfigureAwait(false);
                }

                Log("Extracting Node.js portable (no installation needed)...");

                // Extract zip
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, nodePortablePath);

                // Clean up temporary zip
                try { File.Delete(zipPath); } catch { }

                // Find extracted folder (node-v20.11.0-win-x64)
                var extractedFolder = Directory.GetDirectories(nodePortablePath)[0];

                // Update PATH to point to this folder
                string nodeBinPath = extractedFolder;
                string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                if (!currentPath.Contains(nodeBinPath))
                {
                    Environment.SetEnvironmentVariable("PATH",
                        nodeBinPath + ";" + currentPath,
                        EnvironmentVariableTarget.Process);
                }

                Log($"Node.js portable installed at: {nodeBinPath}");
                Log("✅ No UAC prompt, no admin needed!");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Node.js portable installation error: {ex.Message}");
                return false;
            }
        }


        #endregion

        #region Git

        /// <summary>
        /// Checks if Git is installed (portable or global)
        /// </summary>
        public bool IsGitInstalled()
        {
            try
            {
                // 1. CHECK PORTABLE VERSION (priority!)
                string gitPortablePath = Path.Combine(_pluginPath, "tools", "git");
                string gitCmdPath = Path.Combine(gitPortablePath, "cmd");

                if (Directory.Exists(gitCmdPath))
                {
                    string gitExe = Path.Combine(gitCmdPath, "git.exe");
                    if (File.Exists(gitExe))
                    {
                        // Add to PATH
                        string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                        if (!currentPath.Contains(gitCmdPath))
                        {
                            Environment.SetEnvironmentVariable("PATH",
                                gitCmdPath + ";" + currentPath,
                                EnvironmentVariableTarget.Process);
                        }

                        Log($"Git portable found at: {gitPortablePath}");
                        return true;
                    }
                }

                // 2. CHECK GLOBAL INSTALLATION
                var paths = new[]
                {
                    @"C:\Program Files\Git\cmd\git.exe",
                    @"C:\Program Files (x86)\Git\cmd\git.exe"
                };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        Log($"Git found at: {path}");
                        return true;
                    }
                }

                // 3. TRY VIA PATH
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    Log($"Git version: {output.Trim()}");
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Installs Git PORTABLE using MinGit .zip (COMPLETELY silent!)
        /// </summary>
        public async Task<bool> InstallGitSilently()
        {
            try
            {
                Log("Downloading MinGit portable (zip version - no window!)...");

                // Local folder for Git portable
                string gitPortablePath = Path.Combine(_pluginPath, "tools", "git");
                Directory.CreateDirectory(gitPortablePath);

                // MinGit URL (minimal Git version, .zip format, ZERO UI!)
                string gitUrl = "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.1/MinGit-2.47.1-64-bit.zip";
                string zipPath = Path.Combine(Path.GetTempPath(), "mingit.zip");

                // Download zip
                using (var client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(gitUrl, zipPath).ConfigureAwait(false);
                }

                Log("Extracting MinGit (completely silent, no window)...");

                // Extract zip directly - WITHOUT running anything, WITHOUT window!
                ZipFile.ExtractToDirectory(zipPath, gitPortablePath);

                // Clean up temporary zip
                try { File.Delete(zipPath); } catch { }

                // MinGit: git.exe is in the cmd folder (same as normal Git)
                string gitCmdPath = Path.Combine(gitPortablePath, "cmd");

                // If cmd doesn't exist, try root
                if (!Directory.Exists(gitCmdPath))
                {
                    gitCmdPath = gitPortablePath;
                }

                // Check if git.exe exists
                string gitExe = Path.Combine(gitCmdPath, "git.exe");
                if (File.Exists(gitExe))
                {
                    // Add to PATH
                    string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
                    if (!currentPath.Contains(gitCmdPath))
                    {
                        Environment.SetEnvironmentVariable("PATH",
                            gitCmdPath + ";" + currentPath,
                            EnvironmentVariableTarget.Process);
                    }

                    Log($"MinGit installed at: {gitPortablePath}");
                    Log("✅ Zero UI! No window! No UAC! Perfect!");
                    return true;
                }

                Log("ERROR: git.exe not found after extraction");
                return false;
            }
            catch (Exception ex)
            {
                Log($"MinGit installation error: {ex.Message}");
                return false;
            }
        }


        #endregion

        #region npm packages

        /// <summary>
        /// Checks if npm packages are installed
        /// </summary>
        public bool AreNpmPackagesInstalled()
        {
            try
            {
                string nodeModulesPath = Path.Combine(_pluginPath, "node", "node_modules");

                // Check if node_modules folder exists and has content
                if (Directory.Exists(nodeModulesPath))
                {
                    var dirs = Directory.GetDirectories(nodeModulesPath);
                    if (dirs.Length > 0)
                    {
                        Log($"npm packages found: {dirs.Length} packages");
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Installs npm packages locally
        /// </summary>
        /// <summary>
        /// Checks if whatsapp-web.js is installed
        /// </summary>
        public bool IsWhatsAppWebJsInstalled()
        {
            try
            {
                string whatsappWebPath = Path.Combine(_pluginPath, "node", "node_modules", "whatsapp-web.js");

                if (Directory.Exists(whatsappWebPath))
                {
                    // Check if it has package.json to confirm valid installation
                    string packageJson = Path.Combine(whatsappWebPath, "package.json");
                    if (File.Exists(packageJson))
                    {
                        Log("whatsapp-web.js library found");
                        return true;
                    }
                }

                Log("whatsapp-web.js library NOT found");
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if Baileys is installed
        /// </summary>
        public bool IsBaileysInstalled()
        {
            try
            {
                string baileysPath = Path.Combine(_pluginPath, "node", "node_modules", "@whiskeysockets", "baileys");

                if (Directory.Exists(baileysPath))
                {
                    // Check if it has package.json to confirm valid installation
                    string packageJson = Path.Combine(baileysPath, "package.json");
                    if (File.Exists(packageJson))
                    {
                        Log("Baileys library found");
                        return true;
                    }
                }

                Log("Baileys library NOT found");
                return false;
            }
            catch
            {
                return false;
            }
        }
        /// </summary>
        public async Task<bool> InstallNpmPackages()
        {
            try
            {
                Log("Installing npm packages...");

                string nodePath = Path.Combine(_pluginPath, "node");

                // Find npm
                string npm = FindNpm();
                if (string.IsNullOrEmpty(npm))
                {
                    Log("ERROR: npm not found!");
                    return false;
                }

                Log($"Using npm: {npm}");

                // Run npm install
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = npm,
                        Arguments = "install",
                        WorkingDirectory = nodePath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();

                // Wait async without blocking UI thread
                var tcs = new TaskCompletionSource<bool>();
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) => tcs.TrySetResult(true);

                // Set timeout (2 minutes)
                var timeoutTask = Task.Delay(120000);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);

                if (completedTask == tcs.Task)
                {
                    // Process exited
                    if (process.ExitCode == 0)
                    {
                        Log("npm packages installed successfully!");
                        return true;
                    }

                    string error = process.StandardError.ReadToEnd();
                    Log($"npm install failed (exit code: {process.ExitCode})");
                    if (!string.IsNullOrEmpty(error))
                        Log($"Error: {error.Substring(0, Math.Min(200, error.Length))}");

                    return false;
                }
                else
                {
                    // Timeout
                    Log("npm install timeout after 2 minutes");
                    try { process.Kill(); } catch { }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"npm install error: {ex.Message}");
                return false;
            }
        }

        private string FindNpm()
        {
            Log("=== FindNpm() Debug ===");

            // 1. SEARCH IN PORTABLE INSTALLATION (PRIORITY!)
            string nodePortablePath = Path.Combine(_pluginPath, "tools", "node");
            Log($"Checking portable path: {nodePortablePath}");

            if (Directory.Exists(nodePortablePath))
            {
                Log($"✅ Portable node folder EXISTS!");
                var subdirs = Directory.GetDirectories(nodePortablePath);
                Log($"Found {subdirs.Length} subdirectories");

                if (subdirs.Length > 0)
                {
                    Log($"First subdir: {subdirs[0]}");
                    string npmCmd = Path.Combine(subdirs[0], "npm.cmd");
                    Log($"Looking for npm.cmd at: {npmCmd}");

                    if (File.Exists(npmCmd))
                    {
                        Log($"✅ Found npm (portable): {npmCmd}");
                        return npmCmd;
                    }
                    else
                    {
                        Log($"❌ npm.cmd NOT FOUND at: {npmCmd}");

                        // DEBUG: List files in folder
                        try
                        {
                            var files = Directory.GetFiles(subdirs[0]);
                            Log($"Files in {subdirs[0]}:");
                            foreach (var f in files.Take(10))
                            {
                                Log($"  - {Path.GetFileName(f)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Error listing files: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Log("❌ No subdirectories found!");
                }
            }
            else
            {
                Log($"❌ Portable node folder does NOT exist: {nodePortablePath}");
            }

            // 2. SEARCH IN GLOBAL INSTALLATIONS
            Log("Checking global installations...");
            var paths = new[]
            {
                @"C:\Program Files\nodejs\npm.cmd",
                @"C:\Program Files (x86)\nodejs\npm.cmd"
            };

            foreach (var path in paths)
            {
                Log($"Checking: {path}");
                if (File.Exists(path))
                {
                    Log($"✅ Found npm (global): {path}");
                    return path;
                }
            }

            // 3. TENTAR VIA PATH
            Log("Trying npm via PATH...");
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "npm",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                if (process.ExitCode == 0)
                {
                    Log($"✅ Found npm in PATH (version: {output.Trim()})");
                    return "npm";
                }
                else
                {
                    Log($"❌ npm command failed (exit code: {process.ExitCode})");
                    if (!string.IsNullOrEmpty(error))
                    {
                        Log($"   Error: {error.Substring(0, Math.Min(100, error.Length))}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Exception trying npm: {ex.Message}");
            }

            Log("❌ ERROR: npm not found anywhere!");
            return null;
        }

        #endregion

        private void Log(string message)
        {
            StatusChanged?.Invoke(this, message);
        }
    }
}
