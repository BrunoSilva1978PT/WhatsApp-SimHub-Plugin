using System;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;
using Newtonsoft.Json;

namespace WhatsAppSimHubPlugin.Core
{
    public class DashboardMerger
    {
        // Merged dashboard names for each VoCore
        public const string MERGED_VOCORE1_NAME = "WhatsApp_merged_vocore1";
        public const string MERGED_VOCORE2_NAME = "WhatsApp_merged_vocore2";

        private readonly Action<string> _log;
        private readonly string _dashTemplatesPath;

        public string DashTemplatesPath => _dashTemplatesPath;

        public DashboardMerger(string dashTemplatesPath, Action<string> log = null)
        {
            _dashTemplatesPath = dashTemplatesPath;
            _log = log;
        }

        /// <summary>
        /// Get the merged dashboard name for a specific VoCore
        /// </summary>
        public static string GetMergedDashboardName(int vocoreNumber)
        {
            return vocoreNumber == 1 ? MERGED_VOCORE1_NAME : MERGED_VOCORE2_NAME;
        }

        /// <summary>
        /// Get the merged dashboard title for a specific VoCore
        /// </summary>
        private static string GetMergedDashboardTitle(int vocoreNumber)
        {
            return vocoreNumber == 1
                ? "WhatsApp Plugin Dash Merged Vocore 1"
                : "WhatsApp Plugin Dash Merged Vocore 2";
        }

        public bool DashboardExists(string dashboardName)
        {
            if (string.IsNullOrEmpty(dashboardName)) return false;
            string dashPath = Path.Combine(_dashTemplatesPath, dashboardName);
            string djsonPath = Path.Combine(_dashTemplatesPath, dashboardName + ".djson");
            return Directory.Exists(dashPath) || File.Exists(djsonPath);
        }

        /// <summary>
        /// Merge dashboards for a specific VoCore
        /// </summary>
        /// <param name="baseDashboardName">User's existing dashboard (base layer)</param>
        /// <param name="overlayDashboardName">WhatsApp plugin dashboard (overlay layer)</param>
        /// <param name="vocoreNumber">VoCore number (1 or 2)</param>
        /// <returns>Name of the merged dashboard, or null on failure</returns>
        public string MergeDashboards(string baseDashboardName, string overlayDashboardName, int vocoreNumber)
        {
            string mergedName = GetMergedDashboardName(vocoreNumber);
            string mergedTitle = GetMergedDashboardTitle(vocoreNumber);

            try
            {
                _log?.Invoke($"Starting dashboard merge for VoCore {vocoreNumber}: {baseDashboardName} + {overlayDashboardName}");
                if (!DashboardExists(baseDashboardName))
                {
                    _log?.Invoke($"Base dashboard not found: {baseDashboardName}");
                    return null;
                }
                if (!DashboardExists(overlayDashboardName))
                {
                    _log?.Invoke($"Overlay dashboard not found: {overlayDashboardName}");
                    return null;
                }
                var baseDash = LoadDashboard(baseDashboardName);
                var overlayDash = LoadDashboard(overlayDashboardName);
                if (baseDash == null || overlayDash == null)
                {
                    _log?.Invoke("Failed to load dashboards");
                    return null;
                }
                var mergedDash = CreateMergedDashboard(baseDash, overlayDash, mergedTitle);
                if (mergedDash == null)
                {
                    _log?.Invoke("Failed to create wrapper dashboard");
                    return null;
                }
                bool saved = SaveMergedDashboard(mergedDash, mergedName);
                if (!saved)
                {
                    _log?.Invoke("Failed to save merged dashboard");
                    return null;
                }
                CopyDashboardResources(baseDashboardName, mergedName);
                CopyDashboardResources(overlayDashboardName, mergedName);
                _log?.Invoke($"Dashboard merge completed: {mergedName}");
                return mergedName;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"MergeDashboards error: {ex.Message}");
                return null;
            }
        }

        private JObject LoadDashboard(string dashboardName)
        {
            try
            {
                string djsonPath = null;
                string folderPath = Path.Combine(_dashTemplatesPath, dashboardName);
                if (Directory.Exists(folderPath))
                {
                    string djsonInFolder = Path.Combine(folderPath, dashboardName + ".djson");
                    if (File.Exists(djsonInFolder))
                        djsonPath = djsonInFolder;
                }
                if (djsonPath == null)
                {
                    string directPath = Path.Combine(_dashTemplatesPath, dashboardName + ".djson");
                    if (File.Exists(directPath))
                        djsonPath = directPath;
                }
                if (djsonPath == null)
                {
                    _log?.Invoke($".djson file not found for dashboard: {dashboardName}");
                    return null;
                }
                _log?.Invoke($"Loading dashboard from: {djsonPath}");
                string jsonContent = File.ReadAllText(djsonPath);
                return JObject.Parse(jsonContent);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"LoadDashboard error ({dashboardName}): {ex.Message}");
                return null;
            }
        }

        private JObject CreateMergedDashboard(JObject dash1, JObject dash2, string mergedTitle)
        {
            try
            {
                var dash1Screens = dash1["Screens"] as JArray;
                var dash2Screens = dash2["Screens"] as JArray;
                if (dash1Screens == null || dash2Screens == null)
                {
                    _log?.Invoke("Could not extract Screens from dashboards");
                    return null;
                }

                _log?.Invoke($"Dash 1: {dash1Screens.Count} screen(s), Dash 2: {dash2Screens.Count} screen(s)");

                // Resolution: MAX of both dashboards
                int dash1Width = dash1["BaseWidth"]?.Value<int>() ?? 850;
                int dash1Height = dash1["BaseHeight"]?.Value<int>() ?? 480;
                int dash2Width = dash2["BaseWidth"]?.Value<int>() ?? 850;
                int dash2Height = dash2["BaseHeight"]?.Value<int>() ?? 480;

                int finalWidth = Math.Max(dash1Width, dash2Width);
                int finalHeight = Math.Max(dash1Height, dash2Height);

                _log?.Invoke($"Dash1: {dash1Width}x{dash1Height}, Dash2: {dash2Width}x{dash2Height}, Final: {finalWidth}x{finalHeight}");

                // Calculate scale factors (only needed if a dash is smaller than final)
                double dash1Scale = 1.0;
                if (finalWidth > dash1Width || finalHeight > dash1Height)
                {
                    dash1Scale = Math.Min(
                        (double)finalWidth / dash1Width,
                        (double)finalHeight / dash1Height
                    );
                    _log?.Invoke($"Scaling dash 1: {dash1Scale:F3}x");
                }

                double dash2Scale = 1.0;
                if (finalWidth > dash2Width || finalHeight > dash2Height)
                {
                    dash2Scale = Math.Min(
                        (double)finalWidth / dash2Width,
                        (double)finalHeight / dash2Height
                    );
                    _log?.Invoke($"Scaling dash 2: {dash2Scale:F3}x");
                }

                // Build merged screens: all screens from dash1 + all screens from dash2
                var screens = new JArray();
                var inGameIndexs = new JArray();
                var idleIndexs = new JArray();
                var pitIndexs = new JArray();

                // Add all screens from dash 1
                AddScreens(dash1Screens, dash1Scale, screens, inGameIndexs, idleIndexs, pitIndexs, "Dash1");

                // Add all screens from dash 2
                AddScreens(dash2Screens, dash2Scale, screens, inGameIndexs, idleIndexs, pitIndexs, "Dash2");

                _log?.Invoke($"Merged dashboard: {screens.Count} total screen(s)");

                // Build merged dashboard
                var merged = new JObject();
                merged["DashboardDebugManager"] = new JObject();
                ((JObject)merged["DashboardDebugManager"])["Maximized"] = false;
                merged["Version"] = 2;
                merged["Id"] = Guid.NewGuid().ToString();
                merged["BackgroundColor"] = dash1["BackgroundColor"]?.ToString() ?? "#FF000000";
                merged["BaseWidth"] = finalWidth;
                merged["BaseHeight"] = finalHeight;
                merged["Screens"] = screens;
                merged["SnapToGrid"] = false;
                merged["HideLabels"] = false;
                merged["ShowForeground"] = true;
                merged["ForegroundOpacity"] = 50.0;
                merged["ShowBackground"] = true;
                merged["BackgroundOpacity"] = 50.0;
                merged["ShowBoundingRectangles"] = false;
                merged["GridSize"] = 10;

                // Merge Images arrays from both dashboards
                var images = new JArray();
                if (dash1["Images"] is JArray img1)
                    foreach (var img in img1) images.Add(img.DeepClone());
                if (dash2["Images"] is JArray img2)
                    foreach (var img in img2) images.Add(img.DeepClone());
                merged["Images"] = images;

                var metadata = new JObject();
                metadata["Author"] = "WhatsApp SimHub Plugin";
                metadata["ScreenCount"] = (double)screens.Count;
                metadata["InGameScreensIndexs"] = inGameIndexs;
                metadata["IdleScreensIndexs"] = idleIndexs;
                metadata["MainPreviewIndex"] = 0;
                metadata["IsOverlay"] = false;
                metadata["Width"] = finalWidth;
                metadata["Height"] = finalHeight;
                metadata["OverlaySizeWarning"] = true;
                metadata["MetadataVersion"] = 2.0;
                metadata["EnableOnDashboardMessaging"] = true;
                metadata["PitScreensIndexs"] = pitIndexs;
                metadata["Title"] = mergedTitle;
                metadata["DashboardVersion"] = "V2.0";
                merged["Metadata"] = metadata;
                merged["ShowOnScreenControls"] = true;
                merged["UseStrictJSIsolation"] = false;
                merged["IsOverlay"] = false;
                merged["EnableClickThroughOverlay"] = true;
                merged["EnableOnDashboardMessaging"] = true;
                return merged;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"CreateMergedDashboard error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clone screens from a source dashboard into the merged screens array.
        /// Scales items if the source resolution is smaller than the merged resolution.
        /// </summary>
        private void AddScreens(JArray sourceScreens, double scale, JArray screens,
            JArray inGameIndexs, JArray idleIndexs, JArray pitIndexs, string label)
        {
            for (int i = 0; i < sourceScreens.Count; i++)
            {
                var screen = sourceScreens[i].DeepClone() as JObject;
                if (screen == null) continue;

                // New unique ScreenId for the merged dashboard
                screen["ScreenId"] = Guid.NewGuid().ToString();

                // Scale all items if necessary
                if (scale != 1.0)
                {
                    var items = screen["Items"] as JArray;
                    if (items != null)
                    {
                        foreach (var item in items)
                            ScaleItem(item, scale);
                    }
                }

                screens.Add(screen);

                // Track screen type indexes
                int idx = screens.Count - 1;
                if (screen["InGameScreen"]?.Value<bool>() == true) inGameIndexs.Add(idx);
                if (screen["IdleScreen"]?.Value<bool>() == true) idleIndexs.Add(idx);
                if (screen["PitScreen"]?.Value<bool>() == true) pitIndexs.Add(idx);

                string name = screen["Name"]?.ToString() ?? $"Screen{i}";
                bool isOverlay = screen["IsOverlayLayer"]?.Value<bool>() == true;
                _log?.Invoke($"  {label} screen {i}: \"{name}\", overlay:{isOverlay}");
            }
        }

        private bool SaveMergedDashboard(JObject dashboard, string dashboardName)
        {
            try
            {
                string dashPath = Path.Combine(_dashTemplatesPath, dashboardName);
                if (Directory.Exists(dashPath))
                {
                    _log?.Invoke($"Removing old merged dashboard: {dashPath}");
                    Directory.Delete(dashPath, true);
                }
                Directory.CreateDirectory(dashPath);
                string djsonPath = Path.Combine(dashPath, dashboardName + ".djson");
                string jsonContent = dashboard.ToString(Formatting.Indented);
                File.WriteAllText(djsonPath, jsonContent);
                _log?.Invoke($"Saved merged dashboard: {djsonPath}");
                return true;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"SaveMergedDashboard error: {ex.Message}");
                return false;
            }
        }

        private void CopyDashboardResources(string sourceDashName, string targetDashName)
        {
            try
            {
                string sourcePath = Path.Combine(_dashTemplatesPath, sourceDashName);
                string targetPath = Path.Combine(_dashTemplatesPath, targetDashName);
                if (!Directory.Exists(sourcePath))
                {
                    _log?.Invoke($"Source dashboard folder not found: {sourcePath}");
                    return;
                }
                if (!Directory.Exists(targetPath))
                {
                    _log?.Invoke($"Target dashboard folder not found: {targetPath}");
                    return;
                }
                foreach (var file in Directory.GetFiles(sourcePath))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals(sourceDashName + ".djson", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string targetFile = Path.Combine(targetPath, fileName);
                    if (!File.Exists(targetFile))
                    {
                        File.Copy(file, targetFile);
                        _log?.Invoke($"Copied: {fileName}");
                    }
                }
                foreach (var dir in Directory.GetDirectories(sourcePath))
                {
                    string dirName = Path.GetFileName(dir);
                    string targetDir = Path.Combine(targetPath, dirName);
                    if (!Directory.Exists(targetDir))
                    {
                        CopyDirectory(dir, targetDir);
                        _log?.Invoke($"Copied folder: {dirName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"CopyDashboardResources warning ({sourceDashName}): {ex.Message}");
            }
        }

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string targetFile = Path.Combine(targetDir, fileName);
                File.Copy(file, targetFile, overwrite: false);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string targetSubDir = Path.Combine(targetDir, dirName);
                CopyDirectory(dir, targetSubDir);
            }
        }




        private void ScaleItem(JToken item, double scale)
        {
            // Everything in pixels = integers

            // Scale position
            if (item["Left"] != null)
                item["Left"] = (int)(item["Left"].Value<double>() * scale);

            if (item["Top"] != null)
                item["Top"] = (int)(item["Top"].Value<double>() * scale);

            // Scale size
            if (item["Width"] != null)
                item["Width"] = (int)(item["Width"].Value<double>() * scale);

            if (item["Height"] != null)
                item["Height"] = (int)(item["Height"].Value<double>() * scale);

            // Scale font size
            if (item["FontSize"] != null)
                item["FontSize"] = (int)(item["FontSize"].Value<double>() * scale);

            // Scale BorderRadius
            if (item["BorderStyle"] != null)
            {
                var border = item["BorderStyle"] as JObject;
                if (border != null)
                {
                    if (border["RadiusTopLeft"] != null)
                        border["RadiusTopLeft"] = (int)(border["RadiusTopLeft"].Value<double>() * scale);
                    if (border["RadiusTopRight"] != null)
                        border["RadiusTopRight"] = (int)(border["RadiusTopRight"].Value<double>() * scale);
                    if (border["RadiusBottomLeft"] != null)
                        border["RadiusBottomLeft"] = (int)(border["RadiusBottomLeft"].Value<double>() * scale);
                    if (border["RadiusBottomRight"] != null)
                        border["RadiusBottomRight"] = (int)(border["RadiusBottomRight"].Value<double>() * scale);
                }
            }

            // Scale Bindings for size/position properties
            var bindings = item["Bindings"] as JObject;
            if (bindings != null)
            {
                ScaleBindingFormula(bindings, "Height", scale);
                ScaleBindingFormula(bindings, "Width", scale);
                ScaleBindingFormula(bindings, "Left", scale);
                ScaleBindingFormula(bindings, "Top", scale);
            }

            // Scale Childrens recursively
            var childrens = item["Childrens"] as JArray;
            if (childrens != null)
            {
                foreach (var child in childrens)
                {
                    ScaleItem(child, scale);
                }
            }
        }

        private void ScaleBindingFormula(JObject bindings, string propertyName, double scale)
        {
            var binding = bindings[propertyName];
            if (binding?["Formula"]?["Expression"] != null)
            {
                string expr = binding["Formula"]["Expression"].ToString();

                // Replace each "return X;" with "return (X) * scale;"
                // Regex to find "return ...;" and wrap with scale
                string scaleStr = scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string scaledExpr = System.Text.RegularExpressions.Regex.Replace(
                    expr,
                    @"return\s+(.+?);",
                    m => $"return ({m.Groups[1].Value}) * {scaleStr};"
                );

                binding["Formula"]["Expression"] = scaledExpr;
            }
        }


    }
}
