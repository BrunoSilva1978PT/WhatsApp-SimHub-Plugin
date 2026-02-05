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
            {
                _log?.Invoke($"[DashboardCheck] Dashboard name is empty");
                return false;
            }

            try
            {
                _log?.Invoke($"[DashboardCheck] Checking if dashboard '{dashboardName}' exists...");

                // Use SimHub public API (fast, no I/O, always up-to-date)
                var metadata = _pluginManager?.GetDashboardMetadata(dashboardName);

                bool exists = metadata != null;
                _log?.Invoke($"[DashboardCheck] Dashboard '{dashboardName}' exists: {exists}");

                return exists;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[DashboardCheck] Error checking dashboard '{dashboardName}': {ex.Message}");
                _log?.Invoke($"[DashboardCheck] Exception type: {ex.GetType().Name}");
                _log?.Invoke($"[DashboardCheck] Stack: {ex.StackTrace}");
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
                _log?.Invoke("[VoCoreManager] Getting all devices...");
                var devicesEnumerable = _pluginManager.GetAllDevices(true);
                if (devicesEnumerable == null)
                {
                    _log?.Invoke("[VoCoreManager] GetAllDevices returned null!");
                    return devices;
                }

                _log?.Invoke($"[VoCoreManager] GetAllDevices returned enumerable (type: {devicesEnumerable.GetType().Name})");

                int totalDevices = 0;

                // Try to enumerate
                var enumerator = devicesEnumerable.GetEnumerator();
                _log?.Invoke($"[VoCoreManager] Got enumerator: {enumerator != null}");

                foreach (var device in devicesEnumerable)
                {
                    totalDevices++;
                    var deviceInstance = device as DeviceInstance;
                    if (deviceInstance == null)
                    {
                        _log?.Invoke($"[VoCoreManager] Device #{totalDevices} - Not DeviceInstance");
                        continue;
                    }

                    // Get basic device info
                    string name = deviceInstance.MainDisplayName;
                    string serial = deviceInstance.ConfiguredSerialNumber();
                    string instanceId = deviceInstance.InstanceId.ToString();

                    _log?.Invoke($"[VoCoreManager] Device #{totalDevices} - Name: '{name}', Serial: '{serial}', InstanceId: '{instanceId}'");

                    if (string.IsNullOrEmpty(name))
                    {
                        _log?.Invoke($"[VoCoreManager] Device #{totalDevices} - Skipped (empty name)");
                        continue;
                    }

                    // Use InstanceId if serial is empty (VoCore doesn't always have serial configured)
                    if (string.IsNullOrEmpty(serial))
                    {
                        _log?.Invoke($"[VoCoreManager] Device #{totalDevices} - Serial empty, using InstanceId as identifier");
                        serial = instanceId;
                    }

                    // Try to get VOCORESettings (filters only VoCores)
                    dynamic dynDevice = device;
                    VOCORESettings vocoreSettings = null;

                    try
                    {
                        vocoreSettings = dynDevice.Settings as VOCORESettings;
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"[VoCoreManager] Device #{totalDevices} - Settings error: {ex.Message}");
                        continue; // Not a VoCore
                    }

                    if (vocoreSettings == null)
                    {
                        _log?.Invoke($"[VoCoreManager] Device #{totalDevices} - Not a VoCore (Settings is not VOCORESettings)");
                        continue; // Not a VoCore
                    }

                    _log?.Invoke($"[VoCoreManager] ✓ VoCore found: '{name}'");

                    // Read current state
                    bool overlayEnabled = vocoreSettings.UseOverlayDashboard;
                    string currentDash = vocoreSettings.CurrentOverlayDashboard?.Dashboard;

                    _log?.Invoke($"[VoCoreManager]   Overlay: {overlayEnabled}, Dashboard: '{currentDash}'");

                    devices.Add(new VoCoreDevice
                    {
                        Name = name,
                        Serial = serial,
                        InformationOverlayEnabled = overlayEnabled,
                        CurrentDashboard = currentDash
                    });
                }

                _log?.Invoke($"[VoCoreManager] Total: {totalDevices} devices, {devices.Count} VoCores");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"GetConnectedDevices error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Configure a specific VoCore device (overlay + dashboard)
        /// </summary>
        /// <param name="serialNumber">Device serial number</param>
        /// <param name="vocoreNumber">VoCore number (1 or 2)</param>
        /// <param name="targetDashboard">Target dashboard from settings (CurrentDash)</param>
        public void ConfigureDevice(string serialNumber, int vocoreNumber, string targetDashboard)
        {
            if (string.IsNullOrEmpty(serialNumber))
            {
                _log?.Invoke("ConfigureDevice: serial number is empty");
                return;
            }

            // Default dashboards for each VoCore
            string defaultDashboard = vocoreNumber == 1 ? "WhatsAppPluginVocore1" : "WhatsAppPluginVocore2";
            string mergedDashboard = DashboardMerger.GetMergedDashboardName(vocoreNumber);

            // If no target specified, use default
            if (string.IsNullOrEmpty(targetDashboard))
            {
                targetDashboard = defaultDashboard;
            }

            try
            {
                // Find device by serial
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                {
                    _log?.Invoke($"ConfigureDevice: device with serial '{serialNumber}' not found");
                    return;
                }

                _log?.Invoke($"Configuring VoCore {vocoreNumber} (serial: {serialNumber})...");

                // STEP 1: Information Overlay must be ON
                if (!vocoreSettings.UseOverlayDashboard)
                {
                    vocoreSettings.UseOverlayDashboard = true;
                    _log?.Invoke("✓ Information Overlay enabled");
                }

                // STEP 2: Get current dashboard in SimHub
                string simhubCurrentDash = vocoreSettings.CurrentOverlayDashboard?.Dashboard;

                // Empty/null → set target dashboard
                if (string.IsNullOrEmpty(simhubCurrentDash))
                {
                    vocoreSettings.CurrentOverlayDashboard.TrySet(targetDashboard);
                    _log?.Invoke($"✓ Dashboard set to '{targetDashboard}' (was empty)");
                    return;
                }

                // Check if current dashboard still exists (user may have deleted it)
                if (!DoesDashboardExist(simhubCurrentDash))
                {
                    _log?.Invoke($"⚠️ Dashboard '{simhubCurrentDash}' no longer exists → setting to '{targetDashboard}'");
                    vocoreSettings.CurrentOverlayDashboard.TrySet(targetDashboard);
                    return;
                }

                // Already using target dashboard → don't touch
                if (simhubCurrentDash == targetDashboard)
                {
                    _log?.Invoke($"✓ Dashboard already '{targetDashboard}'");
                    return;
                }

                // Currently using merged dashboard but target is different → switch to target
                if (simhubCurrentDash == mergedDashboard)
                {
                    vocoreSettings.CurrentOverlayDashboard.TrySet(targetDashboard);
                    _log?.Invoke($"✓ Dashboard changed from merged to '{targetDashboard}'");
                    return;
                }

                // SimHub has a different dashboard (user changed it manually in Information Overlay)
                // Merge it with our target (target goes on top as overlay)
                _log?.Invoke($"✓ Found different dashboard '{simhubCurrentDash}' → merging with '{targetDashboard}'...");

                // Change dashboard name to merged FIRST (instant)
                vocoreSettings.CurrentOverlayDashboard.TrySet(mergedDashboard);
                _log?.Invoke($"✓ Dashboard changed to '{mergedDashboard}'");

                // Do actual merge in background (don't wait)
                _ = Task.Run(() =>
                {
                    try
                    {
                        _dashboardMerger.MergeDashboards(simhubCurrentDash, targetDashboard, vocoreNumber);
                        _log?.Invoke($"✓ Dashboard merge completed in background for VoCore {vocoreNumber}");
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"Dashboard merge error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _log?.Invoke($"ConfigureDevice error: {ex.Message}");
            }
        }

        /// <summary>
        /// Set dashboard directly without any checks (used when user changes via dropdown)
        /// </summary>
        public void SetDashboardDirect(string serialNumber, string dashboardName)
        {
            if (string.IsNullOrEmpty(serialNumber) || string.IsNullOrEmpty(dashboardName))
                return;

            try
            {
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                vocoreSettings.CurrentOverlayDashboard.TrySet(dashboardName);
                _log?.Invoke($"✓ Dashboard set directly to '{dashboardName}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"SetDashboardDirect error: {ex.Message}");
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
                    _log?.Invoke($"✓ Deleted merged dashboard: {mergedDashboard}");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Could not delete merged dashboard: {ex.Message}");
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
