using System;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;
using Newtonsoft.Json;

namespace WhatsAppSimHubPlugin.Core
{
    public class DashboardMerger
    {
        private const string MERGED_DASHBOARD_NAME = "WhatsApp_merged_overlay_dash";
        private readonly Action<string> _log;
        private readonly string _dashTemplatesPath;

        public DashboardMerger(string dashTemplatesPath, Action<string> log = null)
        {
            _dashTemplatesPath = dashTemplatesPath;
            _log = log;
        }

        public bool DashboardExists(string dashboardName)
        {
            if (string.IsNullOrEmpty(dashboardName)) return false;
            string dashPath = Path.Combine(_dashTemplatesPath, dashboardName);
            string djsonPath = Path.Combine(_dashTemplatesPath, dashboardName + ".djson");
            return Directory.Exists(dashPath) || File.Exists(djsonPath);
        }

        public string MergeDashboards(string baseDashboardName, string overlayDashboardName)
        {
            try
            {
                _log?.Invoke($"Starting dashboard merge: {baseDashboardName} + {overlayDashboardName}");
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
                var mergedDash = CreateWrapperDashboard(baseDash, overlayDash, baseDashboardName, overlayDashboardName);
                if (mergedDash == null)
                {
                    _log?.Invoke("Failed to create wrapper dashboard");
                    return null;
                }
                bool saved = SaveMergedDashboard(mergedDash, MERGED_DASHBOARD_NAME);
                if (!saved)
                {
                    _log?.Invoke("Failed to save merged dashboard");
                    return null;
                }
                CopyDashboardResources(baseDashboardName, MERGED_DASHBOARD_NAME);
                CopyDashboardResources(overlayDashboardName, MERGED_DASHBOARD_NAME);
                _log?.Invoke($"Dashboard merge completed: {MERGED_DASHBOARD_NAME}");
                return MERGED_DASHBOARD_NAME;
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

        private JObject CreateWrapperDashboard(JObject baseDash, JObject overlayDash, string baseDashName, string overlayDashName)
        {
            try
            {
                var baseItems = baseDash["Screens"]?[0]?["Items"] as JArray;
                var overlayItems = overlayDash["Screens"]?[0]?["Items"] as JArray;
                if (baseItems == null || overlayItems == null)
                {
                    _log?.Invoke("Could not extract Items from dashboards");
                    return null;
                }
                _log?.Invoke($"Base dashboard has {baseItems.Count} items");
                _log?.Invoke($"Overlay dashboard has {overlayItems.Count} items");

                // Obter dimensões originais
                int baseWidth = baseDash["BaseWidth"]?.Value<int>() ?? 850;
                int baseHeight = baseDash["BaseHeight"]?.Value<int>() ?? 480;

                // Resolução nativa do plugin overlay (sempre 850x480)
                const int OVERLAY_NATIVE_WIDTH = 850;
                const int OVERLAY_NATIVE_HEIGHT = 480;

                // ✅ NOVA LÓGICA: Resolução final é SEMPRE a MAIOR entre base e overlay
                int finalWidth = Math.Max(baseWidth, OVERLAY_NATIVE_WIDTH);
                int finalHeight = Math.Max(baseHeight, OVERLAY_NATIVE_HEIGHT);

                _log?.Invoke($"Base dashboard: {baseWidth}x{baseHeight}");
                _log?.Invoke($"Overlay dashboard: {OVERLAY_NATIVE_WIDTH}x{OVERLAY_NATIVE_HEIGHT}");
                _log?.Invoke($"Final merged resolution (MAX): {finalWidth}x{finalHeight}");

                // Calcular scale para cada layer
                double baseScale = 1.0;
                double baseLeft = 0.0;
                double baseTop = 0.0;

                double overlayScale = 1.0;
                double overlayLeft = 0.0;
                double overlayTop = 0.0;

                // Se final > base, escalar base dashboard
                if (finalWidth > baseWidth || finalHeight > baseHeight)
                {
                    baseScale = Math.Min(
                        (double)finalWidth / baseWidth,
                        (double)finalHeight / baseHeight
                    );
                    _log?.Invoke($"Scaling base dashboard: {baseScale:F3}x");
                }

                // Se final > overlay, escalar overlay dashboard
                if (finalWidth > OVERLAY_NATIVE_WIDTH || finalHeight > OVERLAY_NATIVE_HEIGHT)
                {
                    overlayScale = Math.Min(
                        (double)finalWidth / OVERLAY_NATIVE_WIDTH,
                        (double)finalHeight / OVERLAY_NATIVE_HEIGHT
                    );
                    _log?.Invoke($"Scaling overlay dashboard: {overlayScale:F3}x");
                }
                // Calcular dimensões do base layer (escaladas se necessário)
                double baseLayerWidth = baseWidth * baseScale;
                double baseLayerHeight = baseHeight * baseScale;

                var baseLayer = new JObject();
                baseLayer["$type"] = "SimHub.Plugins.OutputPlugins.GraphicalDash.Models.Layer, SimHub.Plugins";
                baseLayer["Name"] = "BaseDashboardLayer";
                baseLayer["Top"] = baseTop;
                baseLayer["Left"] = baseLeft;
                baseLayer["Height"] = baseLayerHeight;
                baseLayer["Width"] = baseLayerWidth;
                baseLayer["BackgroundColor"] = baseDash["BackgroundColor"]?.ToString() ?? "#FF000000";
                baseLayer["Visible"] = true;
                baseLayer["Group"] = false;

                // Clonar e escalar base items se necessário
                var clonedBaseItems = new JArray();
                foreach (var item in baseItems)
                {
                    var cloned = item.DeepClone();

                    // Escalar item se base foi escalado
                    if (baseScale != 1.0)
                    {
                        ScaleItem(cloned, baseScale);
                    }

                    clonedBaseItems.Add(cloned);
                }
                baseLayer["Childrens"] = clonedBaseItems;

                // Calcular dimensões do overlay layer (escaladas se necessário)
                double layerWidth = OVERLAY_NATIVE_WIDTH * overlayScale;
                double layerHeight = OVERLAY_NATIVE_HEIGHT * overlayScale;

                var overlayLayer = new JObject();
                overlayLayer["$type"] = "SimHub.Plugins.OutputPlugins.GraphicalDash.Models.Layer, SimHub.Plugins";
                overlayLayer["Name"] = "OverlayLayer";
                overlayLayer["Top"] = overlayTop;
                overlayLayer["Left"] = overlayLeft;
                overlayLayer["Height"] = layerHeight;
                overlayLayer["Width"] = layerWidth;
                overlayLayer["BackgroundColor"] = "#00FFFFFF";
                overlayLayer["Visible"] = true;
                overlayLayer["Group"] = false;

                // Clonar e escalar overlay items
                var clonedOverlayItems = new JArray();
                foreach (var item in overlayItems)
                {
                    var cloned = item.DeepClone();

                    // Escalar item (X, Y, Width, Height) se necessário
                    if (overlayScale != 1.0)
                    {
                        ScaleItem(cloned, overlayScale);
                    }

                    clonedOverlayItems.Add(cloned);
                }
                overlayLayer["Childrens"] = clonedOverlayItems;

                var bindings = new JObject();
                var visibleBinding = new JObject();
                var formula = new JObject();
                formula["Expression"] = "return $prop(\"WhatsAppPlugin.showmessage\")";
                formula["JSExt"] = 1;
                formula["Interpreter"] = 1;
                visibleBinding["Formula"] = formula;
                visibleBinding["Mode"] = 2;
                visibleBinding["TargetPropertyName"] = "Visible";
                bindings["Visible"] = visibleBinding;
                overlayLayer["Bindings"] = bindings;
                var wrapper = new JObject();
                wrapper["DashboardDebugManager"] = new JObject();
                ((JObject)wrapper["DashboardDebugManager"])["Maximized"] = false;
                wrapper["Version"] = 2;
                wrapper["Id"] = Guid.NewGuid().ToString();
                wrapper["BackgroundColor"] = "#00FFFFFF";
                wrapper["BaseWidth"] = finalWidth;
                wrapper["BaseHeight"] = finalHeight;
                var screens = new JArray();
                var screen = new JObject();
                screen["Name"] = "MainScreen";
                screen["InGameScreen"] = true;
                screen["IdleScreen"] = false;
                screen["PitScreen"] = false;
                screen["ScreenId"] = Guid.NewGuid().ToString();
                screen["IsForegroundLayer"] = false;
                screen["IsBackgroundLayer"] = false;
                screen["BackgroundColor"] = "#00FFFFFF";
                screen["Background"] = "None";
                var items = new JArray();
                items.Add(baseLayer);
                items.Add(overlayLayer);
                screen["Items"] = items;
                screens.Add(screen);
                wrapper["Screens"] = screens;
                wrapper["SnapToGrid"] = false;
                wrapper["HideLabels"] = false;
                wrapper["ShowForeground"] = true;
                wrapper["ForegroundOpacity"] = 50.0;
                wrapper["ShowBackground"] = true;
                wrapper["BackgroundOpacity"] = 50.0;
                wrapper["ShowBoundingRectangles"] = false;
                wrapper["GridSize"] = 10;
                wrapper["Images"] = baseDash["Images"] ?? new JArray();
                var metadata = new JObject();
                metadata["Author"] = "WhatsApp SimHub Plugin";
                metadata["ScreenCount"] = 1.0;
                var inGameIndexs = new JArray();
                inGameIndexs.Add(0);
                metadata["InGameScreensIndexs"] = inGameIndexs;
                metadata["IdleScreensIndexs"] = new JArray();
                metadata["MainPreviewIndex"] = 0;
                metadata["IsOverlay"] = false;
                metadata["Width"] = baseWidth;
                metadata["Height"] = baseHeight;
                metadata["OverlaySizeWarning"] = true;
                metadata["MetadataVersion"] = 2.0;
                metadata["EnableOnDashboardMessaging"] = true;
                metadata["PitScreensIndexs"] = new JArray();
                metadata["Title"] = "WhatsApp SimHub Plugin Merged Dash";
                metadata["DashboardVersion"] = "V2.0";
                wrapper["Metadata"] = metadata;
                wrapper["ShowOnScreenControls"] = true;
                wrapper["UseStrictJSIsolation"] = false;
                wrapper["IsOverlay"] = false;
                wrapper["EnableClickThroughOverlay"] = true;
                wrapper["EnableOnDashboardMessaging"] = true;
                _log?.Invoke("Created wrapper dashboard with 2 layers");
                return wrapper;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"CreateWrapperDashboard error: {ex.Message}");
                return null;
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
            // Tudo em pixels = inteiros

            // Escalar posição
            if (item["Left"] != null)
                item["Left"] = (int)(item["Left"].Value<double>() * scale);

            if (item["Top"] != null)
                item["Top"] = (int)(item["Top"].Value<double>() * scale);

            // Escalar tamanho
            if (item["Width"] != null)
                item["Width"] = (int)(item["Width"].Value<double>() * scale);

            if (item["Height"] != null)
                item["Height"] = (int)(item["Height"].Value<double>() * scale);

            // Escalar font size
            if (item["FontSize"] != null)
                item["FontSize"] = (int)(item["FontSize"].Value<double>() * scale);

            // Escalar BorderRadius
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

            // Escalar Bindings de propriedades de tamanho/posição
            var bindings = item["Bindings"] as JObject;
            if (bindings != null)
            {
                ScaleBindingFormula(bindings, "Height", scale);
                ScaleBindingFormula(bindings, "Width", scale);
                ScaleBindingFormula(bindings, "Left", scale);
                ScaleBindingFormula(bindings, "Top", scale);
            }

            // Escalar Childrens recursivamente
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

                // Substituir cada "return X;" por "return (X) * scale;"
                // Regex para encontrar "return ...;" e envolver com scale
                string scaleStr = scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string scaledExpr = System.Text.RegularExpressions.Regex.Replace(
                    expr,
                    @"return\s+(.+?);",
                    m => $"return ({m.Groups[1].Value}) * {scaleStr};"
                );

                binding["Formula"]["Expression"] = scaledExpr;
            }
        }

        private string WrapFormulaWithScale(string expression, double scale)
        {
            // Multiplicar o resultado final da fórmula por scale
            // Isto funciona para qualquer fórmula JavaScript sem ter que parseá-la
            string trimmed = expression.Trim();

            // Remover ponto e vírgula final se existir
            if (trimmed.EndsWith(";"))
                trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();

            // Se tem múltiplas linhas/statements, envolver a última expressão após o último return
            int lastReturnIndex = trimmed.LastIndexOf("return ", StringComparison.OrdinalIgnoreCase);
            if (lastReturnIndex >= 0)
            {
                string beforeLastReturn = trimmed.Substring(0, lastReturnIndex);
                string afterReturn = trimmed.Substring(lastReturnIndex + 7).Trim();
                return $"{beforeLastReturn}return ({afterReturn}) * {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)};";
            }

            // Fallback: se não tem return, envolver toda a expressão
            return $"return ({trimmed}) * {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)};";
        }
        public static string MergedDashboardName => MERGED_DASHBOARD_NAME;
    }
}
