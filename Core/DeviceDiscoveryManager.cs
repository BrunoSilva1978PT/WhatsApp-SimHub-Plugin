using System;
using System.Collections.Generic;
using SimHub.Plugins;
using SimHub.Plugins.Devices;
using SimHub.Plugins.OutputPlugins.GraphicalDash;
using SimHub.Plugins.OutputPlugins.GraphicalDash.LedModules;
using SimHub.Plugins.DataPlugins.RGBDriver;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver.Settings;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Unified device discovery for all SimHub device types.
    /// Handles standalone and composite device iteration in one place,
    /// extracting VoCore (VOCORESettings) and LED (LedModuleDevice) sub-devices.
    /// </summary>
    public class DeviceDiscoveryManager
    {
        private readonly PluginManager _pluginManager;
        private readonly Action<string> _log;

        public DeviceDiscoveryManager(PluginManager pluginManager, Action<string> log = null)
        {
            _pluginManager = pluginManager;
            _log = log;
        }

        #region Core Iteration

        /// <summary>
        /// Iterates all SimHub devices (standalone + composite sub-devices).
        /// Calls the visitor for each individual device/sub-device.
        /// Parameters: (rawDevice, deviceInstance, parentName)
        /// </summary>
        private void IterateAllDevices(Action<object, DeviceInstance, string> onDevice)
        {
            var devicesEnumerable = _pluginManager.GetAllDevices(true);
            if (devicesEnumerable == null) return;

            foreach (var device in devicesEnumerable)
            {
                var deviceInstance = device as DeviceInstance;
                if (deviceInstance == null) continue;

                string parentName = deviceInstance.MainDisplayName ?? "";

                // Try standalone device
                onDevice(device, deviceInstance, parentName);

                // Try composite sub-devices (wheels, DDUs with embedded VoCore/LED modules)
                try
                {
                    dynamic dynDevice = device;
                    System.Collections.IEnumerable subDevices = dynDevice.Devices;
                    if (subDevices == null) continue;

                    foreach (var subDev in subDevices)
                    {
                        var subInstance = subDev as DeviceInstance;
                        if (subInstance == null) continue;
                        onDevice(subDev, subInstance, parentName);
                    }
                }
                catch
                {
                    // Not a composite device or no Devices property
                }
            }
        }

        #endregion

        #region VoCore Discovery

        /// <summary>
        /// Get all connected VoCore devices with their current state.
        /// </summary>
        public List<VoCoreDevice> GetVoCoreDevices()
        {
            var result = new List<VoCoreDevice>();

            try
            {
                IterateAllDevices((device, instance, parentName) =>
                {
                    var vocore = TryCreateVoCoreDevice(device, instance);
                    if (vocore != null)
                        result.Add(vocore);
                });
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Discovery] GetVoCoreDevices error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Find a VoCore's VOCORESettings by its ConnectedId (serial).
        /// </summary>
        public VOCORESettings FindVoCoreBySerial(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
                return null;

            VOCORESettings found = null;

            try
            {
                IterateAllDevices((device, instance, parentName) =>
                {
                    if (found != null) return;

                    VOCORESettings settings = null;
                    try
                    {
                        dynamic dynDevice = device;
                        settings = dynDevice.Settings as VOCORESettings;
                    }
                    catch
                    {
                        return;
                    }

                    if (settings == null) return;

                    string deviceId = GetConnectedId(settings);
                    if (!string.IsNullOrEmpty(deviceId) && deviceId == serialNumber)
                        found = settings;
                });
            }
            catch
            {
                // Silently fail
            }

            return found;
        }

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
        /// Get VoCore hardware Screen ID (ConnectedId). Never changes between reboots.
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

        #endregion

        #region LED Module Discovery

        /// <summary>
        /// Get all LedModule devices (from standalone and composite devices).
        /// Does NOT include Arduino or Hue devices (those use different APIs).
        /// </summary>
        public List<DiscoveredLedDevice> GetLedModuleDevices()
        {
            var result = new List<DiscoveredLedDevice>();

            try
            {
                var devicesEnumerable = _pluginManager.GetAllDevices(true);
                if (devicesEnumerable == null) return result;

                foreach (var device in devicesEnumerable)
                {
                    // Check inside composite devices (most wheels/boxes are composites)
                    var composite = device as CompositeDeviceInstance;
                    if (composite?.Devices != null)
                    {
                        foreach (var sub in composite.Devices)
                        {
                            var ledDevice = sub as LedModuleDevice;
                            if (ledDevice == null) continue;
                            AddLedModuleDevice(ledDevice, composite.MainDisplayName, result);
                        }
                    }

                    // Also check top-level LedModuleDevices (some appear standalone)
                    var deviceInstance = device as DeviceInstance;
                    if (deviceInstance == null) continue;
                    var topLed = deviceInstance as LedModuleDevice;
                    if (topLed != null)
                    {
                        AddLedModuleDevice(topLed, topLed.MainDisplayName, result);
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[Discovery] GetLedModuleDevices error: {ex.Message}");
            }

            _log?.Invoke($"[Discovery] Found {result.Count} LedModule device(s)");
            return result;
        }

        private void AddLedModuleDevice(LedModuleDevice ledDevice, string parentName, List<DiscoveredLedDevice> result)
        {
            var settings = ledDevice.ledModuleSettings;
            if (settings == null) return;

            // Check for RGB LEDs
            RGBLedsDriver ledDriver = settings.RawDriver ?? settings.LedsDriver;
            if (ledDriver?.Settings != null)
            {
                int rawCount = settings.RawLedCount ?? 0;
                int ledCount = rawCount > 0 ? rawCount : settings.Ledcount;
                if (ledCount > 0)
                {
                    string deviceId = $"led_{parentName}_{ledCount}";
                    result.Add(new DiscoveredLedDevice
                    {
                        DeviceId = deviceId,
                        DeviceName = parentName,
                        DeviceType = LedDeviceType.LedDevice,
                        LedCount = ledCount,
                        LedModuleRef = ledDevice
                    });
                }
            }

            // Check for Matrix
            var matrixDriver = settings.MatrixDriver;
            if (matrixDriver?.Settings != null)
            {
                try
                {
                    var colorArray = matrixDriver.GetResult(0, Rotation.Normal);
                    if (colorArray != null && colorArray.Length > 0)
                    {
                        int totalPixels = colorArray.Length;
                        int side = (int)Math.Sqrt(totalPixels);
                        int rows = side, cols = side;
                        if (side * side != totalPixels)
                        {
                            rows = totalPixels > 0 ? 1 : 0;
                            cols = totalPixels;
                        }

                        string deviceId = $"matrix_{parentName}_{rows}x{cols}";
                        result.Add(new DiscoveredLedDevice
                        {
                            DeviceId = deviceId,
                            DeviceName = parentName,
                            DeviceType = LedDeviceType.DeviceMatrix,
                            LedCount = totalPixels,
                            MatrixRows = rows,
                            MatrixColumns = cols,
                            LedModuleRef = ledDevice
                        });
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[Discovery] Error detecting matrix for {parentName}: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
