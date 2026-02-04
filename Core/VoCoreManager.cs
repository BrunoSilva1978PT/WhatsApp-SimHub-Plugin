using System;
using System.Collections.Generic;
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
        public void ConfigureDevice(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
            {
                _log?.Invoke("ConfigureDevice: serial number is empty");
                return;
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

                _log?.Invoke($"Configuring VoCore (serial: {serialNumber})...");

                // STEP 1: Information Overlay
                if (!vocoreSettings.UseOverlayDashboard)
                {
                    vocoreSettings.UseOverlayDashboard = true;
                    _log?.Invoke("✓ Information Overlay enabled");
                }

                // STEP 2: Dashboard
                string currentDash = vocoreSettings.CurrentOverlayDashboard?.Dashboard;

                // Empty/null → set WhatsAppPlugin
                if (string.IsNullOrEmpty(currentDash))
                {
                    vocoreSettings.CurrentOverlayDashboard.TrySet("WhatsAppPlugin");
                    _log?.Invoke("✓ Dashboard set to 'WhatsAppPlugin' (was empty)");
                    return;
                }

                // Already WhatsAppPlugin → don't touch
                if (currentDash == "WhatsAppPlugin")
                {
                    _log?.Invoke("✓ Dashboard already 'WhatsAppPlugin'");
                    return;
                }

                // Already merged → don't touch
                if (currentDash == "WhatsApp_merged_overlay_dash")
                {
                    _log?.Invoke("✓ Dashboard already merged");
                    return;
                }

                // Other dashboard → merge
                _log?.Invoke($"✓ Found user dashboard '{currentDash}' → merging...");

                // Change dashboard name FIRST (instant)
                vocoreSettings.CurrentOverlayDashboard.TrySet("WhatsApp_merged_overlay_dash");
                _log?.Invoke("✓ Dashboard changed to 'WhatsApp_merged_overlay_dash'");

                // Do actual merge in background (don't wait)
                _ = Task.Run(() =>
                {
                    try
                    {
                        _dashboardMerger.MergeDashboards(currentDash, "WhatsAppPlugin");
                        _log?.Invoke("✓ Dashboard merge completed in background");
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
