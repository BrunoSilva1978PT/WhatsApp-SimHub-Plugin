using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SimHub.Plugins;
using SimHub.Plugins.OutputPlugins.GraphicalDash;
using SimHub.Plugins.Devices;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Manages VoCore devices configuration (zero reflection, direct property access)
    /// Simplified: User controls dashboard via UI, backend only ensures overlay is ON and applies correct dashboard
    /// </summary>
    public class VoCoreManager
    {
        private readonly PluginManager _pluginManager;
        private readonly Action<string> _log;
        private readonly DashboardMerger _dashboardMerger;

        public VoCoreManager(PluginManager pluginManager, DashboardMerger dashboardMerger, Action<string> log = null)
        {
            _pluginManager = pluginManager;
            _dashboardMerger = dashboardMerger;
            _log = log;
        }

        /// <summary>
        /// Check if a dashboard exists in SimHub (uses official API, zero I/O, zero reflection)
        /// </summary>
        public bool DoesDashboardExist(string dashboardName)
        {
            if (string.IsNullOrEmpty(dashboardName))
                return false;

            try
            {
                var metadata = _pluginManager?.GetDashboardMetadata(dashboardName);
                return metadata != null;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DashboardCheck] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get all connected VoCore devices with their current state
        /// </summary>
        public List<VoCoreDevice> GetConnectedDevices()
        {
            var devices = new List<VoCoreDevice>();

            try
            {
                var devicesEnumerable = _pluginManager.GetAllDevices(true);
                if (devicesEnumerable == null)
                    return devices;

                foreach (var device in devicesEnumerable)
                {
                    var deviceInstance = device as DeviceInstance;
                    if (deviceInstance == null)
                        continue;

                    string name = deviceInstance.MainDisplayName;
                    string serial = deviceInstance.ConfiguredSerialNumber();
                    string instanceId = deviceInstance.InstanceId.ToString();

                    if (string.IsNullOrEmpty(name))
                        continue;

                    // Use InstanceId if serial is empty
                    if (string.IsNullOrEmpty(serial))
                        serial = instanceId;

                    // Try to get VOCORESettings (filters only VoCores)
                    dynamic dynDevice = device;
                    VOCORESettings vocoreSettings = null;

                    try
                    {
                        vocoreSettings = dynDevice.Settings as VOCORESettings;
                    }
                    catch
                    {
                        continue; // Not a VoCore
                    }

                    if (vocoreSettings == null)
                        continue;

                    bool overlayEnabled = vocoreSettings.UseOverlayDashboard;
                    string currentDash = vocoreSettings.CurrentOverlayDashboard?.Dashboard;

                    devices.Add(new VoCoreDevice
                    {
                        Name = name,
                        Serial = serial,
                        InformationOverlayEnabled = overlayEnabled,
                        CurrentDashboard = currentDash
                    });
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"GetConnectedDevices error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Ensure VoCore has Information Overlay ON and correct dashboard
        /// Called periodically from DataUpdate()
        /// </summary>
        /// <param name="serialNumber">Device serial number</param>
        /// <param name="expectedDashboard">Dashboard that should be active (from settings)</param>
        public void EnsureConfiguration(string serialNumber, string expectedDashboard)
        {
            if (string.IsNullOrEmpty(serialNumber) || string.IsNullOrEmpty(expectedDashboard))
                return;

            try
            {
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                // Ensure Information Overlay is ON
                if (!vocoreSettings.UseOverlayDashboard)
                {
                    vocoreSettings.UseOverlayDashboard = true;
                    _log?.Invoke($"[EnsureConfig] Information Overlay enabled for '{serialNumber}'");
                }

                // Ensure correct dashboard is set
                string currentDash = vocoreSettings.CurrentOverlayDashboard?.Dashboard;
                if (currentDash != expectedDashboard)
                {
                    // Only change if the expected dashboard exists
                    if (DoesDashboardExist(expectedDashboard))
                    {
                        vocoreSettings.CurrentOverlayDashboard.TrySet(expectedDashboard);
                        _log?.Invoke($"[EnsureConfig] Dashboard set to '{expectedDashboard}' for '{serialNumber}'");
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"EnsureConfiguration error: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply a dashboard directly (1 layer mode)
        /// Deletes merged dashboard if exists
        /// </summary>
        public void ApplyDirect(string serialNumber, int vocoreNumber, string dashboardName)
        {
            if (string.IsNullOrEmpty(serialNumber) || string.IsNullOrEmpty(dashboardName))
                return;

            try
            {
                // Delete merged dashboard if exists
                DeleteMergedDashboard(vocoreNumber);

                // Set dashboard directly
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                vocoreSettings.UseOverlayDashboard = true;
                vocoreSettings.CurrentOverlayDashboard.TrySet(dashboardName);
                _log?.Invoke($"[ApplyDirect] VoCore {vocoreNumber}: Dashboard set to '{dashboardName}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"ApplyDirect error: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply merged dashboard (2 layers mode)
        /// Creates merge of Layer1 (base) + Layer2 (overlay on top)
        /// </summary>
        public void ApplyMerged(string serialNumber, int vocoreNumber, string layer1Dashboard, string layer2Dashboard)
        {
            if (string.IsNullOrEmpty(serialNumber) ||
                string.IsNullOrEmpty(layer1Dashboard) ||
                string.IsNullOrEmpty(layer2Dashboard))
                return;

            try
            {
                string mergedDashboard = DashboardMerger.GetMergedDashboardName(vocoreNumber);
                string mergedDashboardPath = Path.Combine(_dashboardMerger.DashTemplatesPath, mergedDashboard);

                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                {
                    _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Device not found!");
                    return;
                }

                // STEP 1: Turn OFF Information Overlay (force SimHub to release cache)
                bool wasOverlayEnabled = vocoreSettings.UseOverlayDashboard;
                string currentDashboard = vocoreSettings.CurrentOverlayDashboard.Dashboard;

                vocoreSettings.UseOverlayDashboard = false;
                _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Overlay disabled (forcing cache clear)");

                // STEP 2: Delete old merged dashboard (now that cache is cleared)
                if (Directory.Exists(mergedDashboardPath))
                {
                    try
                    {
                        Directory.Delete(mergedDashboardPath, true);
                        _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Deleted old merged dashboard");
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[ApplyMerged] Warning: Could not delete old merged dashboard: {ex.Message}");
                    }
                }

                // STEP 3: Copy current dashboard to merged location (assets only, no .djson)
                if (!string.IsNullOrEmpty(currentDashboard) && currentDashboard != mergedDashboard)
                {
                    try
                    {
                        string currentDashPath = Path.Combine(_dashboardMerger.DashTemplatesPath, currentDashboard);
                        if (Directory.Exists(currentDashPath))
                        {
                            CopyDirectory(currentDashPath, mergedDashboardPath);
                            _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Copied '{currentDashboard}' assets to '{mergedDashboard}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[ApplyMerged] Warning: Could not copy current dashboard: {ex.Message}");
                    }
                }

                // STEP 4: Do merge (creates new .djson with correct name)
                _dashboardMerger.MergeDashboards(layer1Dashboard, layer2Dashboard, vocoreNumber);
                _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Merge completed ('{layer1Dashboard}' + '{layer2Dashboard}')");

                // STEP 5: Turn overlay back ON with the merged dashboard
                vocoreSettings.UseOverlayDashboard = true;
                vocoreSettings.CurrentOverlayDashboard.TrySet(mergedDashboard);
                _log?.Invoke($"[ApplyMerged] VoCore {vocoreNumber}: Overlay enabled with '{mergedDashboard}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"ApplyMerged error: {ex.Message}");
            }
        }

        /// <summary>
        /// Copy directory recursively (excluding .djson files)
        /// </summary>
        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            // Copy all files EXCEPT .djson (merge will create the correct .djson)
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                // Skip .djson files - the merge process will create the correct one
                if (Path.GetExtension(file).Equals(".djson", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            // Copy subdirectories recursively
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        /// <summary>
        /// Check if merged dashboard exists for a specific VoCore
        /// </summary>
        public bool MergedDashboardExists(int vocoreNumber)
        {
            try
            {
                string mergedDashboard = DashboardMerger.GetMergedDashboardName(vocoreNumber);
                string mergedPath = Path.Combine(_dashboardMerger.DashTemplatesPath, mergedDashboard);
                return Directory.Exists(mergedPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Delete merged dashboard for a specific VoCore
        /// </summary>
        public void DeleteMergedDashboard(int vocoreNumber)
        {
            try
            {
                string mergedDashboard = DashboardMerger.GetMergedDashboardName(vocoreNumber);
                string mergedPath = Path.Combine(_dashboardMerger.DashTemplatesPath, mergedDashboard);

                if (Directory.Exists(mergedPath))
                {
                    Directory.Delete(mergedPath, true);
                    _log?.Invoke($"[DeleteMerged] Deleted: {mergedDashboard}");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"DeleteMergedDashboard error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the expected dashboard name for a VoCore based on settings
        /// </summary>
        public string GetExpectedDashboard(int vocoreNumber, int layerCount)
        {
            if (layerCount == 2)
            {
                // 2 layers = use merged dashboard
                return DashboardMerger.GetMergedDashboardName(vocoreNumber);
            }
            else
            {
                // 1 layer = return null, caller should use Layer1 from settings
                return null;
            }
        }

        /// <summary>
        /// Clear the Information Overlay dashboard (set to empty)
        /// Called when user deselects a VoCore from a slot
        /// </summary>
        public void ClearOverlayDashboard(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
                return;

            try
            {
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                vocoreSettings.CurrentOverlayDashboard.TrySet("");
                _log?.Invoke($"[ClearOverlay] Dashboard cleared for '{serialNumber}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"ClearOverlayDashboard error: {ex.Message}");
            }
        }

        /// <summary>
        /// Find VoCore device by serial number
        /// </summary>
        private VOCORESettings FindDeviceBySerial(string serialNumber)
        {
            try
            {
                var devicesEnumerable = _pluginManager.GetAllDevices(true);
                if (devicesEnumerable == null) return null;

                foreach (var device in devicesEnumerable)
                {
                    var deviceInstance = device as DeviceInstance;
                    if (deviceInstance == null) continue;

                    string serial = deviceInstance.ConfiguredSerialNumber();

                    // If serial is empty, use InstanceId instead
                    if (string.IsNullOrEmpty(serial))
                        serial = deviceInstance.InstanceId.ToString();

                    if (serial != serialNumber) continue;

                    // Found device! Get settings
                    dynamic dynDevice = device;
                    try
                    {
                        return dynDevice.Settings as VOCORESettings;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}
