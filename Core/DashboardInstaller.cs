using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Automatically installs the WhatsApp dashboard in SimHub.
    /// Extracts .simhubdash (ZIP) to DashTemplates - SimHub auto-detects via FileSystemWatcher.
    /// </summary>
    public class DashboardInstaller
    {
        private const string DASHBOARD_FILENAME = "WhatsAppPlugin.simhubdash";
        private const string DASHBOARD_NAME = "WhatsAppPlugin";

        private const string OVERLAY_DASHBOARD_FILENAME = "Simhub WhatsApp Plugin Overlay.simhubdash";
        private const string OVERLAY_DASHBOARD_NAME = "Simhub WhatsApp Plugin Overlay";
        private readonly Action<string> _log;

        public DashboardInstaller(Action<string> log = null)
        {
            _log = log;
        }

        /// <summary>
        /// Installs (extracts) the dashboard automatically.
        /// Only installs if the WhatsAppPlugin folder does NOT exist in DashTemplates.
        /// This allows the user to make manual updates without losing changes.
        /// </summary>
        public bool InstallDashboard()
        {
            try
            {
                if (IsDashboardInstalled())
                {
                    _log?.Invoke("Dashboard already exists - skipping installation (allows manual updates)");
                    return true;
                }

                _log?.Invoke("Dashboard not found - installing from resources...");
                return ExtractDashboardFromResources(DASHBOARD_FILENAME, DASHBOARD_NAME);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Failed to install dashboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Install a specific dashboard from Resources by filename.
        /// </summary>
        public bool InstallDashboard(string fileName)
        {
            try
            {
                string dashboardName = Path.GetFileNameWithoutExtension(fileName);

                string dashboardsPath = GetDashboardsPath();
                if (string.IsNullOrEmpty(dashboardsPath))
                {
                    _log?.Invoke("Could not find DashTemplates folder");
                    return false;
                }

                string targetFolder = Path.Combine(dashboardsPath, dashboardName);
                if (Directory.Exists(targetFolder))
                {
                    _log?.Invoke($"Dashboard '{dashboardName}' already installed - skipping");
                    return true;
                }

                _log?.Invoke($"Installing dashboard '{dashboardName}' from resources...");
                return ExtractDashboardFromResources(fileName, dashboardName);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"InstallDashboard error for '{fileName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Installs the overlay dashboard (for VR, etc.) if it doesn't exist.
        /// </summary>
        public bool InstallOverlayDashboard()
        {
            try
            {
                if (IsOverlayDashboardInstalled())
                {
                    _log?.Invoke("Overlay dashboard already exists - skipping installation");
                    return true;
                }

                _log?.Invoke("Overlay dashboard not found - installing from resources...");
                return ExtractDashboardFromResources(OVERLAY_DASHBOARD_FILENAME, OVERLAY_DASHBOARD_NAME);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Failed to install overlay dashboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extracts a .simhubdash (ZIP) from embedded resources to DashTemplates.
        /// SimHub auto-detects new dashboards via FileSystemWatcher.
        /// </summary>
        private bool ExtractDashboardFromResources(string resourceFileName, string dashboardName)
        {
            string dashboardsPath = GetDashboardsPath();
            if (string.IsNullOrEmpty(dashboardsPath))
            {
                _log?.Invoke("Could not find DashTemplates folder");
                return false;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"WhatsAppSimHubPlugin.Resources.{resourceFileName}";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    _log?.Invoke($"Dashboard resource not found: {resourceName}");
                    return false;
                }

                // .simhubdash is a ZIP that already contains the dashboard folder inside
                string tempZipFile = Path.Combine(Path.GetTempPath(), resourceFileName);

                using (FileStream fileStream = File.Create(tempZipFile))
                {
                    stream.CopyTo(fileStream);
                }

                try
                {
                    _log?.Invoke($"Extracting dashboard to: {dashboardsPath}");
                    ZipFile.ExtractToDirectory(tempZipFile, dashboardsPath);
                }
                finally
                {
                    try { File.Delete(tempZipFile); } catch { }
                }

                string targetFolder = Path.Combine(dashboardsPath, dashboardName);
                if (Directory.Exists(targetFolder))
                {
                    _log?.Invoke($"Dashboard '{dashboardName}' installed successfully");
                    return true;
                }

                _log?.Invoke($"Dashboard folder not found after extraction: {targetFolder}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the dashboard is installed (folder exists in DashTemplates).
        /// </summary>
        public bool IsDashboardInstalled()
        {
            try
            {
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
                _log?.Invoke($"IsDashboardInstalled error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the overlay dashboard is installed.
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

        /// <summary>
        /// Gets the path to SimHub's dashboards folder.
        /// </summary>
        public string GetDashboardsPath()
        {
            try
            {
                // SimHub installation folder (where the executable is)
                string simHubExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string simHubFolder = Path.GetDirectoryName(simHubExePath);
                string dashTemplatesPath = Path.Combine(simHubFolder, "DashTemplates");

                if (Directory.Exists(dashTemplatesPath))
                    return dashTemplatesPath;

                // AppData fallback
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appDataDashPath = Path.Combine(appDataPath, "SimHub", "DashboardTemplates");

                if (Directory.Exists(appDataDashPath))
                    return appDataDashPath;

                // Create in installation folder
                Directory.CreateDirectory(dashTemplatesPath);
                _log?.Invoke($"Created DashTemplates folder: {dashTemplatesPath}");
                return dashTemplatesPath;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"GetDashboardsPath error: {ex.Message}");
                return null;
            }
        }

        public static string DashboardName => Path.GetFileNameWithoutExtension(DASHBOARD_FILENAME);
    }
}
