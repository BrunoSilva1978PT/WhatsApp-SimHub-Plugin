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
        /// Get all connected VoCore devices with their current state.
        /// Supports standalone VoCores and VoCores embedded in composite devices (wheels, DDUs).
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

                    // Try direct device first (standalone VoCore)
                    var vocoreDevice = TryCreateVoCoreDevice(device, deviceInstance);
                    if (vocoreDevice != null)
                    {
                        devices.Add(vocoreDevice);
                        continue;
                    }

                    // If CompositeDevice, check sub-devices (wheels, DDUs with embedded VoCore)
                    try
                    {
                        dynamic dynDevice = device;
                        System.Collections.IEnumerable subDevices = dynDevice.Devices;
                        if (subDevices == null)
                            continue;

                        foreach (var subDev in subDevices)
                        {
                            var subInstance = subDev as DeviceInstance;
                            if (subInstance == null)
                                continue;

                            var subVoCoreDevice = TryCreateVoCoreDevice(subDev, subInstance);
                            if (subVoCoreDevice != null)
                                devices.Add(subVoCoreDevice);
                        }
                    }
                    catch
                    {
                        // Not a composite device or no Devices property
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"GetConnectedDevices error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Try to create a VoCoreDevice from a device instance.
        /// Returns null if the device doesn't have VOCORESettings.
        /// </summary>
        private VoCoreDevice TryCreateVoCoreDevice(object device, DeviceInstance deviceInstance)
        {
            string name = deviceInstance.MainDisplayName;
            if (string.IsNullOrEmpty(name))
                return null;

            VOCORESettings vocoreSettings = null;
            try
            {
                dynamic dynDevice = device;
                vocoreSettings = dynDevice.Settings as VOCORESettings;
            }
            catch
            {
                return null;
            }

            if (vocoreSettings == null)
                return null;

            string serial = GetConnectedId(vocoreSettings);
            if (string.IsNullOrEmpty(serial))
                return null;

            return new VoCoreDevice
            {
                Name = name,
                Serial = serial,
                InformationOverlayEnabled = vocoreSettings.UseOverlayDashboard,
                CurrentDashboard = vocoreSettings.CurrentOverlayDashboard?.Dashboard
            };
        }

        /// <summary>
        /// Get VoCore hardware Screen ID (ConnectedId). Always available on any VoCore device.
        /// Returns null if not a valid VoCore (no BitmapDisplayInstance or no ConnectedId).
        /// </summary>
        private string GetConnectedId(VOCORESettings vocoreSettings)
        {
            try
            {
                dynamic bdi = vocoreSettings.BitmapDisplayInstance;
                if (bdi != null)
                    return bdi.ConnectedId;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Ensure VoCore has Information Overlay ON and correct dashboard
        /// Called periodically from DataUpdate()
        /// </summary>
        /// <param name="serialNumber">Device serial number</param>
        /// <param name="expectedDashboard">Dashboard that should be active (from settings)</param>
        public void EnsureOverlayEnabled(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
                return;

            try
            {
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                // Only ensure Information Overlay is ON - don't touch the dashboard
                if (!vocoreSettings.UseOverlayDashboard)
                {
                    vocoreSettings.UseOverlayDashboard = true;
                    _log?.Invoke($"[EnsureOverlay] Information Overlay enabled for '{serialNumber}'");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"EnsureOverlayEnabled error: {ex.Message}");
            }
        }

        /// <summary>
        /// Set a dashboard on a VoCore and enable overlay
        /// </summary>
        public void SetDashboard(string serialNumber, int vocoreNumber, string dashboardName)
        {
            if (string.IsNullOrEmpty(serialNumber) || string.IsNullOrEmpty(dashboardName))
                return;

            try
            {
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                    return;

                vocoreSettings.UseOverlayDashboard = true;
                vocoreSettings.CurrentOverlayDashboard.Dashboard = dashboardName;
                _log?.Invoke($"[SetDashboard] VoCore {vocoreNumber}: Dashboard set to '{dashboardName}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"SetDashboard error: {ex.Message}");
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
                vocoreSettings.CurrentOverlayDashboard.Dashboard = mergedDashboard;
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
        /// Clear the Information Overlay dashboard (set to empty)
        /// Called when user deselects a VoCore from a slot
        /// Only clears the dashboard - does not disable overlay or change any other settings
        /// </summary>
        public void ClearOverlayDashboard(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
            {
                _log?.Invoke($"[ClearOverlay] Serial number is empty, skipping");
                return;
            }

            try
            {
                VOCORESettings vocoreSettings = FindDeviceBySerial(serialNumber);
                if (vocoreSettings == null)
                {
                    _log?.Invoke($"[ClearOverlay] Device not found for '{serialNumber}'");
                    return;
                }

                // Disable overlay and clear dashboard directly
                vocoreSettings.UseOverlayDashboard = false;
                vocoreSettings.CurrentOverlayDashboard.Dashboard = null;
                _log?.Invoke($"[ClearOverlay] Overlay disabled and dashboard set to null for '{serialNumber}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"ClearOverlayDashboard error: {ex.Message}");
            }
        }

        /// <summary>
        /// Find VoCore device by serial number.
        /// Searches standalone devices and sub-devices inside composite devices.
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

                    // Try direct device first (standalone VoCore)
                    var result = TryMatchDeviceBySerial(device, deviceInstance, serialNumber);
                    if (result != null)
                        return result;

                    // If CompositeDevice, check sub-devices
                    try
                    {
                        dynamic dynDevice = device;
                        System.Collections.IEnumerable subDevices = dynDevice.Devices;
                        if (subDevices == null) continue;

                        foreach (var subDev in subDevices)
                        {
                            var subInstance = subDev as DeviceInstance;
                            if (subInstance == null) continue;

                            result = TryMatchDeviceBySerial(subDev, subInstance, serialNumber);
                            if (result != null)
                                return result;
                        }
                    }
                    catch
                    {
                        // Not a composite device
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Try to match a device by serial/ConnectedId and return its VOCORESettings.
        /// </summary>
        private VOCORESettings TryMatchDeviceBySerial(object device, DeviceInstance deviceInstance, string serialNumber)
        {
            VOCORESettings vocoreSettings = null;
            try
            {
                dynamic dynDevice = device;
                vocoreSettings = dynDevice.Settings as VOCORESettings;
            }
            catch
            {
                return null;
            }

            if (vocoreSettings == null)
                return null;

            string deviceId = GetConnectedId(vocoreSettings);
            if (string.IsNullOrEmpty(deviceId) || deviceId != serialNumber)
                return null;

            return vocoreSettings;
        }
    }
}
