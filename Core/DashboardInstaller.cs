using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Instala automaticamente o dashboard WhatsApp no SimHub
    /// </summary>
    public class DashboardInstaller
    {
        private const string DASHBOARD_FILENAME = "WhatsAppPlugin.simhubdash";
        private const string DASHBOARD_NAME = "WhatsAppPlugin";
        private readonly Action<string> _log;
        private readonly object _pluginManager;

        public DashboardInstaller(object pluginManager, Action<string> log = null)
        {
            _pluginManager = pluginManager;
            _log = log;
        }

        /// <summary>
        /// Instala (extrai) o dashboard automaticamente
        /// SEMPRE reinstala para garantir que está atualizado!
        /// </summary>
        public bool InstallDashboard()
        {
            try
            {
                _log?.Invoke("📦 Installing/Updating WhatsApp dashboard...");

                // Extrair dashboard do recurso embebido para ficheiro temporário
                string tempDashFile = ExtractDashboardToTemp();
                if (string.IsNullOrEmpty(tempDashFile))
                {
                    _log?.Invoke("❌ Failed to extract dashboard from resources");
                    return false;
                }

                // Tentar importar via DashboardManager (raramente funciona)
                bool imported = ImportDashboardViaManager(tempDashFile);

                if (imported)
                {
                    // Limpar ficheiro temporário
                    try
                    {
                        if (File.Exists(tempDashFile))
                            File.Delete(tempDashFile);
                    }
                    catch { }

                    _log?.Invoke($"✅ WhatsApp dashboard installed successfully!");
                    return true;
                }

                // Usar método de extração direta (sempre funciona)
                bool extracted = InstallDashboardFallback();

                if (extracted)
                {
                    // Limpar ficheiro temporário
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
                _log?.Invoke($"❌ Failed to install dashboard: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extrai dashboard do recurso embebido para ficheiro temporário
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

                    // Criar ficheiro temporário
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
        /// Importa dashboard usando DashboardManager do SimHub (método oficial!)
        /// Nota: DashboardManager não está disponível via PluginManager, então usa fallback
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
                    // DashboardManager não disponível - usar fallback (extração direta)
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
        /// Tenta importar usando uma instância de DashboardManager
        /// </summary>
        private bool TryImportWithManager(object dashboardManager, string dashboardFilePath)
        {
            try
            {
                var dashboardManagerType = dashboardManager.GetType();
                _log?.Invoke($"✅ Got DashboardManager instance: {dashboardManagerType.Name}");

                // Tentar método ImportDashboard (usado pelo Lovely Dashboard Plugin)
                var importMethod = dashboardManagerType.GetMethod("ImportDashboard",
                    new Type[] { typeof(string) });

                if (importMethod != null)
                {
                    _log?.Invoke($"✅ Found ImportDashboard method, importing...");
                    var result = importMethod.Invoke(dashboardManager, new object[] { dashboardFilePath });
                    _log?.Invoke($"✅ ImportDashboard returned: {result}");
                    return true;
                }

                // Fallback: Tentar método ImportDashboardFromFile
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
        /// Fallback: Extrai dashboard para DashTemplates (SimHub reconhece automaticamente!)
        /// </summary>
        private bool InstallDashboardFallback()
        {
            try
            {
                string dashboardsPath = GetDashboardsPath();
                if (string.IsNullOrEmpty(dashboardsPath))
                    return false;

                // IMPORTANTE: .simhubdash é um ZIP!
                // O ZIP JÁ TEM uma pasta "WhatsAppPlugin" dentro
                // Então extraímos DIRETAMENTE para DashTemplates!
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"WhatsAppSimHubPlugin.Resources.{DASHBOARD_FILENAME}";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return false;

                    // Criar ficheiro temporário
                    string tempZipFile = Path.Combine(Path.GetTempPath(), DASHBOARD_FILENAME);

                    using (FileStream fileStream = File.Create(tempZipFile))
                    {
                        stream.CopyTo(fileStream);
                    }

                    // Verificar se pasta já existe
                    string targetFolder = Path.Combine(dashboardsPath, DASHBOARD_NAME);
                    if (Directory.Exists(targetFolder))
                    {
                        _log?.Invoke($"🗑️ Removing old dashboard folder: {targetFolder}");
                        Directory.Delete(targetFolder, true);
                    }

                    // EXTRAIR diretamente para DashTemplates
                    // (O ZIP já contém a pasta WhatsAppPlugin dentro)
                    _log?.Invoke($"📦 Extracting dashboard to: {dashboardsPath}");
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZipFile, dashboardsPath);

                    // Limpar ficheiro temporário
                    try
                    {
                        File.Delete(tempZipFile);
                    }
                    catch { }

                    // Verificar se pasta foi criada
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
        /// Verifica se o dashboard está instalado (pasta extraída em DashTemplates)
        /// </summary>
        public bool IsDashboardInstalled()
        {
            try
            {
                // Verificar se PASTA existe em DashTemplates
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
        /// Obtém o caminho da pasta de dashboards do SimHub
        /// </summary>
        public string GetDashboardsPath()
        {
            try
            {
                // OPÇÃO 1: Pasta de instalação do SimHub (onde está o executável)
                // Normalmente: C:\Program Files (x86)\SimHub\DashTemplates
                string simHubExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                string simHubFolder = Path.GetDirectoryName(simHubExePath);
                string dashTemplatesPath = Path.Combine(simHubFolder, "DashTemplates");

                if (Directory.Exists(dashTemplatesPath))
                {
                    return dashTemplatesPath;
                }

                // OPÇÃO 2: AppData (fallback, caso SimHub use este em vez do acima)
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appDataDashPath = Path.Combine(appDataPath, "SimHub", "DashboardTemplates");

                if (Directory.Exists(appDataDashPath))
                {
                    return appDataDashPath;
                }

                // OPÇÃO 3: Tentar criar na pasta de instalação
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
        /// Nome do dashboard (sem extensão)
        /// </summary>
        public static string DashboardName => Path.GetFileNameWithoutExtension(DASHBOARD_FILENAME);
    }
}
