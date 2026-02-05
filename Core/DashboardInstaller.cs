using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Automatically installs the WhatsApp dashboard in SimHub
    /// </summary>
    public class DashboardInstaller
    {
        private const string DASHBOARD_FILENAME = "WhatsAppPlugin.simhubdash";
        private const string DASHBOARD_NAME = "WhatsAppPlugin";

        private const string OVERLAY_DASHBOARD_FILENAME = "Simhub WhatsApp Plugin Overlay.simhubdash";
        private const string OVERLAY_DASHBOARD_NAME = "Simhub WhatsApp Plugin Overlay";
        private readonly Action<string> _log;
        private readonly object _pluginManager;

        public DashboardInstaller(object pluginManager, Action<string> log = null)
        {
            _pluginManager = pluginManager;
            _log = log;
        }

        /// <summary>
        /// Installs (extracts) the dashboard automatically
        /// Only installs if the WhatsAppPlugin folder does NOT exist in DashTemplates
        /// This allows the user to make manual updates without losing changes
        /// </summary>
        public bool InstallDashboard()
        {
            try
            {
                // Check if dashboard already exists - if so, don't reinstall
                if (IsDashboardInstalled())
                {
                    _log?.Invoke("Dashboard already exists - skipping installation (allows manual updates)");
                    return true;
                }

                _log?.Invoke("Dashboard not found - installing from resources...");

                // Extract dashboard from embedded resource to temporary file
                string tempDashFile = ExtractDashboardToTemp();
                if (string.IsNullOrEmpty(tempDashFile))
                {
                    _log?.Invoke("Failed to extract dashboard from resources");
                    return false;
                }

                // Try to import via DashboardManager (rarely works)
                bool imported = ImportDashboardViaManager(tempDashFile);

                if (imported)
                {
                    // Clean up temporary file
                    try
                    {
                        if (File.Exists(tempDashFile))
                            File.Delete(tempDashFile);
                    }
                    catch { }

                    _log?.Invoke($"WhatsApp dashboard installed successfully!");
                    return true;
                }

                // Use direct extraction method (always works)
                bool extracted = InstallDashboardFallback();

                if (extracted)
                {
                    // Clean up temporary file
                    try
                    {
                        if (File.Exists(tempDashFile))
                            File.Delete(tempDashFile);
                    }
                    catch { }
                }

                return extracted;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Failed to install dashboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extracts dashboard from embedded resource to temporary file
        /// </summary>
        private string ExtractDashboardToTemp()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"WhatsAppSimHubPlugin.Resources.{DASHBOARD_FILENAME}";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        _log?.Invoke($"❌ Dashboard resource not found: {resourceName}");
                        return null;
                    }

                    // Create temporary file
                    string tempPath = Path.GetTempPath();
                    string tempFile = Path.Combine(tempPath, DASHBOARD_FILENAME);

                    using (FileStream fileStream = File.Create(tempFile))
                    {
                        stream.CopyTo(fileStream);
                    }

                    _log?.Invoke($"✅ Dashboard extracted to: {tempFile}");
                    return tempFile;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ ExtractDashboardToTemp error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Imports dashboard using SimHub's DashboardManager (official method!)
        /// Note: DashboardManager is not available via PluginManager, so uses fallback
        /// </summary>
        private bool ImportDashboardViaManager(string dashboardFilePath)
        {
            try
            {
                if (_pluginManager == null)
                {
                    return false;
                }

                var pluginManagerType = _pluginManager.GetType();
                var dashboardManagerProp = pluginManagerType.GetProperty("DashboardManager");

                if (dashboardManagerProp == null)
                {
                    // DashboardManager not available - use fallback (direct extraction)
                    return false;
                }

                var dashboardManager = dashboardManagerProp.GetValue(_pluginManager);
                if (dashboardManager == null)
                {
                    return false;
                }

                return TryImportWithManager(dashboardManager, dashboardFilePath);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ ImportDashboardViaManager error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tries to import using a DashboardManager instance
        /// </summary>
        private bool TryImportWithManager(object dashboardManager, string dashboardFilePath)
        {
            try
            {
                var dashboardManagerType = dashboardManager.GetType();
                _log?.Invoke($"✅ Got DashboardManager instance: {dashboardManagerType.Name}");

                // Try ImportDashboard method (used by Lovely Dashboard Plugin)
                var importMethod = dashboardManagerType.GetMethod("ImportDashboard",
                    new Type[] { typeof(string) });

                if (importMethod != null)
                {
                    _log?.Invoke($"✅ Found ImportDashboard method, importing...");
                    var result = importMethod.Invoke(dashboardManager, new object[] { dashboardFilePath });
                    _log?.Invoke($"✅ ImportDashboard returned: {result}");
                    return true;
                }

                // Fallback: Try ImportDashboardFromFile method
                importMethod = dashboardManagerType.GetMethod("ImportDashboardFromFile",
                    new Type[] { typeof(string) });

                if (importMethod != null)
                {
                    _log?.Invoke($"✅ Found ImportDashboardFromFile method, importing...");
                    var result = importMethod.Invoke(dashboardManager, new object[] { dashboardFilePath });
                    _log?.Invoke($"✅ ImportDashboardFromFile returned: {result}");
                    return true;
                }

                _log?.Invoke("❌ No import method found in DashboardManager");
                _log?.Invoke("Available methods:");
                foreach (var method in dashboardManagerType.GetMethods())
                {
                    if (method.Name.Contains("Import") || method.Name.Contains("Dashboard"))
                        _log?.Invoke($"   - {method.Name}");
                }

                return false;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ ImportDashboardViaManager error: {ex.Message}");
                _log?.Invoke($"   Stack: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Fallback: Extracts dashboard to DashTemplates (SimHub auto-detects it!)
        /// </summary>
        private bool InstallDashboardFallback()
        {
            try
            {
                string dashboardsPath = GetDashboardsPath();
                if (string.IsNullOrEmpty(dashboardsPath))
                    return false;

                // IMPORTANT: .simhubdash is a ZIP!
                // The ZIP ALREADY HAS a "WhatsAppPlugin" folder inside
                // So we extract DIRECTLY to DashTemplates!
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"WhatsAppSimHubPlugin.Resources.{DASHBOARD_FILENAME}";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return false;

                    // Create temporary file
                    string tempZipFile = Path.Combine(Path.GetTempPath(), DASHBOARD_FILENAME);

                    using (FileStream fileStream = File.Create(tempZipFile))
                    {
                        stream.CopyTo(fileStream);
                    }

                    // Check target folder (should not exist, already verified in InstallDashboard)
                    string targetFolder = Path.Combine(dashboardsPath, DASHBOARD_NAME);

                    // EXTRACT directly to DashTemplates
                    // (The ZIP already contains the WhatsAppPlugin folder inside)
                    _log?.Invoke($"📦 Extracting dashboard to: {dashboardsPath}");
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZipFile, dashboardsPath);

                    // Clean up temporary file
                    try
                    {
                        File.Delete(tempZipFile);
                    }
                    catch { }

                    // Check if folder was created
                    if (Directory.Exists(targetFolder))
                    {
                        _log?.Invoke($"✅ Dashboard extracted successfully!");
                        _log?.Invoke($"   Folder: {targetFolder}");
                        _log?.Invoke($"   SimHub should auto-detect it now!");
                        return true;
                    }
                    else
                    {
                        _log?.Invoke($"❌ Dashboard folder not found after extraction: {targetFolder}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Fallback install failed: {ex.Message}");
                _log?.Invoke($"   Stack: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Install a specific dashboard from Resources by filename
        /// </summary>
        /// <param name="fileName">The .simhubdash filename (e.g., "WhatsAppPluginVocore1.simhubdash")</param>
        public bool InstallDashboard(string fileName)
        {
            try
            {
                // Get dashboard name without extension
                string dashboardName = Path.GetFileNameWithoutExtension(fileName);

                string dashboardsPath = GetDashboardsPath();
                if (string.IsNullOrEmpty(dashboardsPath))
                {
                    _log?.Invoke($"Could not find DashTemplates folder");
                    return false;
                }

                // Check if already installed
                string targetFolder = Path.Combine(dashboardsPath, dashboardName);
                if (Directory.Exists(targetFolder))
                {
                    _log?.Invoke($"Dashboard '{dashboardName}' already installed - skipping");
                    return true;
                }

                _log?.Invoke($"Installing dashboard '{dashboardName}' from resources...");

                // Extract from embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"WhatsAppSimHubPlugin.Resources.{fileName}";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        _log?.Invoke($"Resource not found: {resourceName}");
                        return false;
                    }

                    // Create temporary file
                    string tempZipFile = Path.Combine(Path.GetTempPath(), fileName);

                    using (FileStream fileStream = File.Create(tempZipFile))
                    {
                        stream.CopyTo(fileStream);
                    }

                    // Extract to DashTemplates
                    ZipFile.ExtractToDirectory(tempZipFile, dashboardsPath);

                    // Clean up temp file
                    try { File.Delete(tempZipFile); } catch { }

                    // Verify installation
                    if (Directory.Exists(targetFolder))
                    {
                        _log?.Invoke($"Dashboard '{dashboardName}' installed successfully");
                        return true;
                    }
                    else
                    {
                        _log?.Invoke($"Dashboard folder not found after extraction: {targetFolder}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"InstallDashboard error for '{fileName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the dashboard is installed (folder extracted in DashTemplates)
        /// </summary>
        public bool IsDashboardInstalled()
        {
            try
            {
                // Check if FOLDER exists in DashTemplates
                string dashboardsPath = GetDashboardsPath();
                if (!string.IsNullOrEmpty(dashboardsPath))
                {
                    string targetFolder = Path.Combine(dashboardsPath, DASHBOARD_NAME);
                    return Directory.Exists(targetFolder);
                }

                return false;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"⚠️ IsDashboardInstalled error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the path to SimHub's dashboards folder
        /// </summary>
        public string GetDashboardsPath()
        {
            try
            {
                // OPTION 1: SimHub installation folder (where the executable is)
                // Usually: C:\Program Files (x86)\SimHub\DashTemplates
                string simHubExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string simHubFolder = Path.GetDirectoryName(simHubExePath);
                string dashTemplatesPath = Path.Combine(simHubFolder, "DashTemplates");

                if (Directory.Exists(dashTemplatesPath))
                {
                    return dashTemplatesPath;
                }

                // OPTION 2: AppData (fallback, in case SimHub uses this instead of above)
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appDataDashPath = Path.Combine(appDataPath, "SimHub", "DashboardTemplates");

                if (Directory.Exists(appDataDashPath))
                {
                    return appDataDashPath;
                }

                // OPTION 3: Try to create in installation folder
                if (!Directory.Exists(dashTemplatesPath))
                {
                    Directory.CreateDirectory(dashTemplatesPath);
                    _log?.Invoke($"✅ Created DashTemplates folder: {dashTemplatesPath}");
                    return dashTemplatesPath;
                }

                _log?.Invoke("❌ Could not find or create DashTemplates folder");
                return null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ GetDashboardsPath error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Dashboard name (without extension)
        /// </summary>
        public static string DashboardName => Path.GetFileNameWithoutExtension(DASHBOARD_FILENAME);

        /// <summary>
        /// Installs the overlay dashboard (for VR, etc.) if it doesn't exist
        /// </summary>
        public bool InstallOverlayDashboard()
        {
            try
            {
                // Check if already exists
                if (IsOverlayDashboardInstalled())
                {
                    _log?.Invoke("Overlay dashboard already exists - skipping installation");
                    return true;
                }

                _log?.Invoke("Overlay dashboard not found - installing from resources...");

                string dashboardsPath = GetDashboardsPath();
                if (string.IsNullOrEmpty(dashboardsPath))
                    return false;

                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"WhatsAppSimHubPlugin.Resources.{OVERLAY_DASHBOARD_FILENAME.Replace(" ", " ")}";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        _log?.Invoke($"❌ Overlay dashboard resource not found: {resourceName}");
                        return false;
                    }

                    // Create temporary file
                    string tempZipFile = Path.Combine(Path.GetTempPath(), OVERLAY_DASHBOARD_FILENAME);

                    using (FileStream fileStream = File.Create(tempZipFile))
                    {
                        stream.CopyTo(fileStream);
                    }

                    // Extract to DashTemplates
                    _log?.Invoke($"📦 Extracting overlay dashboard to: {dashboardsPath}");
                    ZipFile.ExtractToDirectory(tempZipFile, dashboardsPath);

                    // Clean up temporary file
                    try { File.Delete(tempZipFile); } catch { }

                    // Check if folder was created
                    string targetFolder = Path.Combine(dashboardsPath, OVERLAY_DASHBOARD_NAME);
                    if (Directory.Exists(targetFolder))
                    {
                        _log?.Invoke($"✅ Overlay dashboard installed successfully!");
                        return true;
                    }
                    else
                    {
                        _log?.Invoke($"❌ Overlay dashboard folder not found after extraction");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"❌ Failed to install overlay dashboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the overlay dashboard is installed
        /// </summary>
        public bool IsOverlayDashboardInstalled()
        {
            try
            {
                string dashboardsPath = GetDashboardsPath();
                if (!string.IsNullOrEmpty(dashboardsPath))
                {
                    string targetFolder = Path.Combine(dashboardsPath, OVERLAY_DASHBOARD_NAME);
                    return Directory.Exists(targetFolder);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
