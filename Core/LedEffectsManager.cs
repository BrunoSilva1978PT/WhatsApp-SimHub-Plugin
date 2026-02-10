using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Newtonsoft.Json;
using SimHub.Plugins;
using SimHub.Plugins.DataPlugins.RGBDriver;
using SimHub.Plugins.DataPlugins.RGBDriver.LedsContainers;
using SimHub.Plugins.DataPlugins.RGBDriver.Settings;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver.Settings;
using SimHub.Plugins.Devices;
using SimHub.Plugins.OutputPlugins.GraphicalDash.LedModules;
using SimHub.Plugins.OutputPlugins.Dash;
using SimHub.Plugins.OutputPlugins.Dash.GLCDTemplating;
using SimHub.Plugins.SimuLight;
using SimHub.Plugins.SimuLight.Models;
using WhatsAppSimHubPlugin.Models;
using LedsScriptedContainer = SimHub.Plugins.DataPlugins.RGBDriver.LedsContainers.ScriptedContentContainer;
using MatrixScriptedContainer = SimHub.Plugins.DataPlugins.RGBMatrixDriver.MatrixContainers.ScriptedContentContainer;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Manages LED notification effects across all device types.
    /// Uses SimHub's Color Effects pipeline injection to control LEDs.
    /// </summary>
    public class LedEffectsManager : IDisposable
    {
        private const int MaxSlots = 8;
        private const int MaxLedsPerSlot = 128;
        private const string PluginPropertyPrefix = "WhatsAppPlugin";
        private const string ContainerDescription = "WhatsApp LED Notifications";

        private readonly Action<string> _log;
        private readonly PluginManager _pluginManager;
        private readonly DeviceDiscoveryManager _discovery;

        // Configurable flash interval (ms) - applies to all effect types
        public int FlashIntervalMs { get; set; } = 250;

        // Slot-based color arrays (read by containers via JS expressions)
        private readonly string[][] _slotColors;

        // Active connections: slot -> connection info
        private readonly Dictionary<int, LedConnection> _connections = new Dictionary<int, LedConnection>();
        private int _nextSlot = 0;

        // Active effects
        private readonly Dictionary<string, ActiveEffect> _activeEffects = new Dictionary<string, ActiveEffect>();
        private readonly object _effectLock = new object();

        // Profile change detection (checked every 5s via DataUpdate)
        private DateTime _lastProfileCheck = DateTime.MinValue;

        public LedEffectsManager(PluginManager pluginManager, DeviceDiscoveryManager discovery, Action<string> log = null)
        {
            _pluginManager = pluginManager;
            _discovery = discovery;
            _log = log;

            _slotColors = new string[MaxSlots][];
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                _slotColors[slot] = new string[MaxLedsPerSlot];
                for (int i = 0; i < MaxLedsPerSlot; i++)
                    _slotColors[slot][i] = "";
            }
        }

        /// <summary>
        /// Returns the slot color value for property registration.
        /// Called by AttachDelegate in the main plugin.
        /// </summary>
        public string GetSlotColor(int slot, int ledIndex)
        {
            if (slot < 0 || slot >= MaxSlots || ledIndex < 0 || ledIndex >= MaxLedsPerSlot)
                return "";
            return _slotColors[slot][ledIndex];
        }

        #region Device Discovery

        /// <summary>
        /// Discovers all available LED devices from SimHub.
        /// LedModule devices use DeviceDiscoveryManager (shared with VoCore discovery).
        /// Arduino and Hue use their own plugin APIs.
        /// </summary>
        public List<DiscoveredLedDevice> DiscoverDevices()
        {
            var result = new List<DiscoveredLedDevice>();

            // LedModule devices (from DeviceDiscoveryManager - same iteration as VoCore)
            try
            {
                var ledModuleDevices = _discovery.GetLedModuleDevices();
                result.AddRange(ledModuleDevices);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[LED] Error discovering LedModule devices: {ex.Message}");
            }

            try
            {
                DiscoverArduinoDevices(result);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[LED] Error discovering Arduino devices: {ex.Message}");
            }

            try
            {
                DiscoverHueDevices(result);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[LED] Error discovering Hue devices: {ex.Message}");
            }

            _log?.Invoke($"[LED] Discovered {result.Count} LED device(s)");
            return result;
        }

        private void DiscoverArduinoDevices(List<DiscoveredLedDevice> result)
        {
            var serialDash = _pluginManager.GetPlugin<SerialDashPlugin>();
            if (serialDash == null) return;

            // Arduino RGB LEDs
            var rgbDriver = serialDash.Settings?.RGBLedsDriver;
            if (rgbDriver?.Settings?.CurrentProfile != null)
            {
                int ledCount = DetectArduinoLedCount(serialDash);
                if (ledCount > 0)
                {
                    result.Add(new DiscoveredLedDevice
                    {
                        DeviceId = "arduino_rgb_leds",
                        DeviceName = "Arduino RGB LEDs",
                        DeviceType = LedDeviceType.ArduinoLeds,
                        LedCount = ledCount
                    });
                }
            }

            // Arduino RGB Matrix
            var matrixDriver = serialDash.Settings?.RGBMatrixDriver;
            if (matrixDriver?.Settings?.CurrentProfile != null)
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

                        result.Add(new DiscoveredLedDevice
                        {
                            DeviceId = "arduino_rgb_matrix",
                            DeviceName = "Arduino RGB Matrix",
                            DeviceType = LedDeviceType.ArduinoMatrix,
                            LedCount = totalPixels,
                            MatrixRows = rows,
                            MatrixColumns = cols
                        });
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[LED] Error detecting Arduino matrix: {ex.Message}");
                }
            }
        }

        private int DetectArduinoLedCount(SerialDashPlugin serialDash)
        {
            try
            {
                int count = serialDash.Settings.RGBLedsDriver.Settings.ForcedLedCount;
                if (count > 0) return count;

                // Sum from all connected Arduinos
                var multiSettings = serialDash.Settings.MultipleArduinoSettings;
                if (multiSettings != null)
                {
                    count = 0;
                    foreach (var arduino in multiSettings)
                        count += arduino.RgbLeds;
                    if (count > 0) return count;
                }

                // Try single Arduino settings
                var singleSettings = serialDash.Settings.SingleArduinoSettingsList;
                if (singleSettings != null)
                {
                    count = 0;
                    foreach (var arduino in singleSettings)
                        count += arduino.RgbLeds;
                    if (count > 0) return count;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[LED] Error detecting Arduino LED count: {ex.Message}");
            }

            return 0;
        }

        private void DiscoverHueDevices(List<DiscoveredLedDevice> result)
        {
            var simuLight = _pluginManager.GetPlugin<SimuLightPlugin>();
            if (simuLight == null) return;

            var devices = simuLight.Settings?.BluetoothSettings?.Devices;
            if (devices == null) return;

            foreach (var device in devices)
            {
                var hueDevice = device as AssociatedHueDevice;
                if (hueDevice == null) continue;

                string deviceId = $"hue_{hueDevice.BridgeID}_{hueDevice.GroupID}";
                int lightCount = hueDevice.AllAreas?.Count ?? 0;

                var lightNames = new List<string>();
                if (hueDevice.AllAreas != null)
                {
                    int lightIndex = 1;
                    foreach (var area in hueDevice.AllAreas)
                    {
                        string name;
                        try
                        {
                            dynamic dynArea = area;
                            name = dynArea.Title as string ?? $"Light {lightIndex}";
                        }
                        catch
                        {
                            name = $"Light {lightIndex}";
                        }
                        lightNames.Add(name);
                        lightIndex++;
                    }
                }

                result.Add(new DiscoveredLedDevice
                {
                    DeviceId = deviceId,
                    DeviceName = hueDevice.Name ?? "Hue Group",
                    DeviceType = LedDeviceType.PhilipsHue,
                    LedCount = lightCount,
                    HueDeviceRef = hueDevice,
                    HueLightNames = lightNames
                });
            }
        }

        #endregion

        #region Container Injection

        /// <summary>
        /// Connects to a device by injecting a ScriptedContentContainer into its Color Effects profile.
        /// Returns the assigned slot index, or -1 on failure.
        /// </summary>
        public int ConnectDevice(DiscoveredLedDevice device)
        {
            if (_nextSlot >= MaxSlots)
            {
                _log?.Invoke($"[LED] Cannot connect {device.DeviceName}: all {MaxSlots} slots in use");
                return -1;
            }

            int slot = _nextSlot;
            string error = null;

            try
            {
                switch (device.DeviceType)
                {
                    case LedDeviceType.LedDevice:
                        error = ConnectLedModule(device, slot);
                        break;
                    case LedDeviceType.DeviceMatrix:
                        error = ConnectDeviceMatrix(device, slot);
                        break;
                    case LedDeviceType.ArduinoLeds:
                        error = ConnectArduinoLeds(device, slot);
                        break;
                    case LedDeviceType.ArduinoMatrix:
                        error = ConnectArduinoMatrix(device, slot);
                        break;
                    case LedDeviceType.PhilipsHue:
                        error = ConnectHue(device, slot);
                        break;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error != null)
            {
                _log?.Invoke($"[LED] Failed to connect {device.DeviceName}: {error}");
                return -1;
            }

            _nextSlot++;
            _log?.Invoke($"[LED] Connected {device.DeviceName} on slot {slot} ({device.LedCount} LEDs)");
            return slot;
        }

        private string ConnectLedModule(DiscoveredLedDevice device, int slot)
        {
            var settings = device.LedModuleRef.ledModuleSettings;

            if (settings.IndividualLEDsMode != IndividualLEDsMode.Exclusive)
                settings.IndividualLEDsMode = IndividualLEDsMode.Exclusive;

            RGBLedsDriver driver = settings.RawDriver ?? settings.LedsDriver;
            if (driver?.Settings == null) return "No LED driver found";

            return InjectLedContainer(driver.Settings, device.LedCount, slot, device.DeviceId);
        }

        private string ConnectDeviceMatrix(DiscoveredLedDevice device, int slot)
        {
            var settings = device.LedModuleRef.ledModuleSettings;
            var matrixDriver = settings.MatrixDriver;
            if (matrixDriver?.Settings == null) return "No matrix driver found";

            return InjectMatrixContainer(matrixDriver.Settings, device.MatrixRows, device.MatrixColumns, slot, device.DeviceId);
        }

        private string ConnectArduinoLeds(DiscoveredLedDevice device, int slot)
        {
            var serialDash = _pluginManager.GetPlugin<SerialDashPlugin>();
            var rgbDriver = serialDash?.Settings?.RGBLedsDriver;
            if (rgbDriver?.Settings == null) return "No Arduino RGB driver found";

            return InjectLedContainer(rgbDriver.Settings, device.LedCount, slot, device.DeviceId);
        }

        private string ConnectArduinoMatrix(DiscoveredLedDevice device, int slot)
        {
            var serialDash = _pluginManager.GetPlugin<SerialDashPlugin>();
            var matrixDriver = serialDash?.Settings?.RGBMatrixDriver;
            if (matrixDriver?.Settings == null) return "No Arduino matrix driver found";

            return InjectMatrixContainer(matrixDriver.Settings, device.MatrixRows, device.MatrixColumns, slot, device.DeviceId);
        }

        private string ConnectHue(DiscoveredLedDevice device, int slot)
        {
            var simuLight = _pluginManager.GetPlugin<SimuLightPlugin>();
            var effectsDriver = simuLight?.Settings?.BluetoothSettings?.EffectsDriver;
            if (effectsDriver?.Settings == null) return "No Hue effects driver found";

            return InjectLedContainer(effectsDriver.Settings, device.LedCount, slot, device.DeviceId);
        }

        private string InjectLedContainer(LedsSettings ledsSettings, int ledCount, int slot, string deviceId)
        {
            var profile = ledsSettings.CurrentProfile;
            if (profile == null) return "No active LED profile";

            int safeLedCount = Math.Min(ledCount, MaxLedsPerSlot);

            string jsExpression =
                $"var colors = []; " +
                $"for (var i = 1; i <= {safeLedCount}; i++) {{ " +
                $"  var c = $prop('{PluginPropertyPrefix}.S{slot}_Led' + i); " +
                $"  if (c && c.length > 0) colors.push(c); " +
                $"  else colors.push('#00000000'); " +
                $"}} " +
                $"return colors;";

            var container = new LedsScriptedContainer();
            container.ContentFormula = new ExpressionValue(jsExpression, Interpreter.Javascript);
            container.LedCount = safeLedCount;
            container.Description = ContainerDescription;
            container.IsEnabled = false;

            profile.LedContainers.Add(container);

            _connections[slot] = new LedConnection
            {
                DeviceId = deviceId,
                Slot = slot,
                LedCount = safeLedCount,
                IsMatrix = false,
                LedContainer = container,
                LedProfile = profile,
                LedsSettings = ledsSettings
            };

            return null;
        }

        private string InjectMatrixContainer(MatrixSettings matrixSettings, int rows, int columns, int slot, string deviceId)
        {
            var profile = matrixSettings.CurrentProfile;
            if (profile == null) return "No active matrix profile";

            int totalLeds = rows * columns;
            int safeLedCount = Math.Min(totalLeds, MaxLedsPerSlot);

            string jsExpression =
                $"var colors = []; " +
                $"for (var i = 1; i <= {safeLedCount}; i++) {{ " +
                $"  var c = $prop('{PluginPropertyPrefix}.S{slot}_Led' + i); " +
                $"  if (c && c.length > 0) colors.push(c); " +
                $"  else colors.push('#00000000'); " +
                $"}} " +
                $"return colors;";

            var container = new MatrixScriptedContainer();
            container.ContentFormula = new ExpressionValue(jsExpression, Interpreter.Javascript);
            container.Rows = rows;
            container.Columns = columns;
            container.Description = ContainerDescription;
            container.IsEnabled = false;

            profile.LedContainers.Add(container);

            _connections[slot] = new LedConnection
            {
                DeviceId = deviceId,
                Slot = slot,
                LedCount = safeLedCount,
                IsMatrix = true,
                MatrixRows = rows,
                MatrixColumns = columns,
                MatrixContainer = container,
                MatrixProfile = profile,
                MatrixSettings = matrixSettings
            };

            return null;
        }

        /// <summary>
        /// Disconnects all devices and removes injected containers.
        /// </summary>
        public void DisconnectAll()
        {
            foreach (var conn in _connections.Values)
            {
                try
                {
                    if (conn.IsMatrix)
                    {
                        conn.MatrixContainer.IsEnabled = false;
                        conn.MatrixProfile?.LedContainers?.Remove(conn.MatrixContainer);
                    }
                    else
                    {
                        conn.LedContainer.IsEnabled = false;
                        conn.LedProfile?.LedContainers?.Remove(conn.LedContainer);
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[LED] Error disconnecting slot {conn.Slot}: {ex.Message}");
                }
            }

            _connections.Clear();
            _nextSlot = 0;

            // Clear all slot colors
            for (int s = 0; s < MaxSlots; s++)
                for (int i = 0; i < MaxLedsPerSlot; i++)
                    _slotColors[s][i] = "";

            _log?.Invoke("[LED] All devices disconnected");
        }

        #endregion

        #region Effects

        /// <summary>
        /// Triggers an LED effect on all enabled devices for the given priority.
        /// </summary>
        public void TriggerEffect(List<LedDeviceConfig> deviceConfigs, string priority, int durationMs)
        {
            if (deviceConfigs == null || deviceConfigs.Count == 0) return;

            lock (_effectLock)
            {
                foreach (var config in deviceConfigs)
                {
                    if (!config.Enabled) continue;

                    // Find the connection for this device
                    LedConnection connection = null;
                    foreach (var conn in _connections.Values)
                    {
                        if (conn.DeviceId == config.DeviceId)
                        {
                            connection = conn;
                            break;
                        }
                    }

                    if (connection == null) continue;

                    // Get color for this priority
                    string color = GetColorForPriority(config, priority);
                    string color2 = GetColor2ForPriority(config, priority);
                    HueEffectType hueEffect = GetHueEffectForPriority(config, priority);
                    bool isHue = config.DeviceType == LedDeviceType.PhilipsHue;
                    bool isMatrix = config.DeviceType == LedDeviceType.DeviceMatrix || config.DeviceType == LedDeviceType.ArduinoMatrix;

                    var effect = new ActiveEffect
                    {
                        DeviceId = config.DeviceId,
                        Slot = connection.Slot,
                        LedCount = connection.LedCount,
                        Color = color,
                        Color2 = color2,
                        IsMatrix = isMatrix,
                        MatrixRows = connection.MatrixRows,
                        MatrixColumns = connection.MatrixColumns,
                        MatrixMode = config.MatrixMode,
                        IsHue = isHue,
                        HueEffect = hueEffect,
                        SelectedLights = isHue ? GetSelectedLightsForPriority(config, priority) : null,
                        TotalLights = connection.LedCount,
                        StartTime = DateTime.Now,
                        DurationMs = durationMs,
                        BlinkOn = true,
                        NextToggle = DateTime.Now.AddMilliseconds(FlashIntervalMs)
                    };

                    _activeEffects[config.DeviceId] = effect;

                    // Enable the container
                    if (connection.IsMatrix)
                        connection.MatrixContainer.IsEnabled = true;
                    else
                        connection.LedContainer.IsEnabled = true;

                    // Set initial colors
                    ApplyEffectFrame(effect);

                    _log?.Invoke($"[LED] Effect started on {config.DeviceName} ({priority}, {durationMs}ms)");
                }
            }
        }

        /// <summary>
        /// Called from DataUpdate (60 FPS) to animate active effects.
        /// </summary>
        public void Update()
        {
            lock (_effectLock)
            {
                if (_activeEffects.Count == 0) return;

                var now = DateTime.Now;
                var expired = new List<string>();

                foreach (var kvp in _activeEffects)
                {
                    var effect = kvp.Value;

                    // Check duration
                    if ((now - effect.StartTime).TotalMilliseconds >= effect.DurationMs)
                    {
                        expired.Add(kvp.Key);
                        continue;
                    }

                    // Check toggle timing
                    int interval = FlashIntervalMs;

                    if (now >= effect.NextToggle)
                    {
                        effect.BlinkOn = !effect.BlinkOn;
                        effect.NextToggle = now.AddMilliseconds(interval);
                        ApplyEffectFrame(effect);
                    }
                }

                // Stop expired effects
                foreach (var deviceId in expired)
                {
                    StopEffect(deviceId);
                }
            }
        }

        /// <summary>
        /// Stops a specific device's effect.
        /// </summary>
        public void StopEffect(string deviceId)
        {
            lock (_effectLock)
            {
                if (!_activeEffects.TryGetValue(deviceId, out var effect))
                    return;

                _activeEffects.Remove(deviceId);

                // Clear slot colors
                int slot = effect.Slot;
                for (int i = 0; i < MaxLedsPerSlot; i++)
                    _slotColors[slot][i] = "";

                // Disable container
                if (_connections.TryGetValue(slot, out var conn))
                {
                    if (conn.IsMatrix)
                        conn.MatrixContainer.IsEnabled = false;
                    else
                        conn.LedContainer.IsEnabled = false;
                }
            }
        }

        /// <summary>
        /// Stops all active effects.
        /// </summary>
        public void StopAllEffects()
        {
            lock (_effectLock)
            {
                var deviceIds = _activeEffects.Keys.ToList();
                foreach (var id in deviceIds)
                    StopEffect(id);
            }
        }

        /// <summary>
        /// Triggers a test effect on a single device.
        /// </summary>
        public void TestEffect(LedDeviceConfig config, string priority, int durationMs = 3000)
        {
            if (config == null) return;

            var testConfig = new List<LedDeviceConfig> { new LedDeviceConfig
            {
                DeviceId = config.DeviceId,
                DeviceName = config.DeviceName,
                DeviceType = config.DeviceType,
                Enabled = true,
                NormalColor = config.NormalColor,
                VipColor = config.VipColor,
                UrgentColor = config.UrgentColor,
                MatrixMode = config.MatrixMode,
                HueNormalEffect = config.HueNormalEffect,
                HueVipEffect = config.HueVipEffect,
                HueUrgentEffect = config.HueUrgentEffect,
                HueColor2Normal = config.HueColor2Normal,
                HueColor2Vip = config.HueColor2Vip,
                HueColor2Urgent = config.HueColor2Urgent,
                SelectedLightsNormal = config.SelectedLightsNormal,
                SelectedLightsVip = config.SelectedLightsVip,
                SelectedLightsUrgent = config.SelectedLightsUrgent
            }};

            TriggerEffect(testConfig, priority, durationMs);
        }

        private void ApplyEffectFrame(ActiveEffect effect)
        {
            int slot = effect.Slot;

            if (effect.IsMatrix && effect.MatrixMode == MatrixDisplayMode.EnvelopeIcon)
            {
                ApplyEnvelopeIcon(effect);
                return;
            }

            if (effect.IsHue && effect.HueEffect == HueEffectType.Alternating)
            {
                ApplyHueAlternating(effect);
                return;
            }

            // Standard flash: all LEDs same color, toggle on/off
            string hexColor = effect.BlinkOn ? ToArgbHex(effect.Color) : "#FF000000";
            var selectedLights = effect.SelectedLights;
            bool hasSelection = effect.IsHue && selectedLights != null && selectedLights.Count > 0;

            for (int i = 0; i < effect.LedCount; i++)
            {
                if (hasSelection && !selectedLights.Contains(i))
                {
                    _slotColors[slot][i] = "#00000000";
                    continue;
                }
                _slotColors[slot][i] = hexColor;
            }
        }

        private void ApplyEnvelopeIcon(ActiveEffect effect)
        {
            int slot = effect.Slot;
            int rows = effect.MatrixRows;
            int cols = effect.MatrixColumns;

            if (rows < 8 || cols < 8)
            {
                // Fallback to flash for small matrices
                string hexColor = effect.BlinkOn ? ToArgbHex(effect.Color) : "#FF000000";
                for (int i = 0; i < effect.LedCount; i++)
                    _slotColors[slot][i] = hexColor;
                return;
            }

            // 8x8 envelope icon pattern (1 = colored, 0 = black)
            // Centered in the matrix if larger than 8x8
            int[] envelopePattern = {
                1,1,1,1,1,1,1,1,
                1,1,0,0,0,0,1,1,
                1,0,1,0,0,1,0,1,
                1,0,0,1,1,0,0,1,
                1,0,0,1,1,0,0,1,
                1,0,0,0,0,0,0,1,
                1,0,0,0,0,0,0,1,
                1,1,1,1,1,1,1,1
            };

            string onColor = effect.BlinkOn ? ToArgbHex(effect.Color) : "#FF000000";
            string offColor = "#FF000000";

            // Clear all pixels
            for (int i = 0; i < effect.LedCount; i++)
                _slotColors[slot][i] = "#FF000000";

            // Calculate offset to center the 8x8 icon
            int offsetRow = (rows - 8) / 2;
            int offsetCol = (cols - 8) / 2;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    int matrixIdx = (offsetRow + r) * cols + (offsetCol + c);
                    if (matrixIdx >= 0 && matrixIdx < effect.LedCount)
                    {
                        _slotColors[slot][matrixIdx] = envelopePattern[r * 8 + c] == 1 ? onColor : offColor;
                    }
                }
            }
        }

        private void ApplyHueAlternating(ActiveEffect effect)
        {
            int slot = effect.Slot;
            string color1 = ToArgbHex(effect.Color);
            string color2 = ToArgbHex(effect.Color2);

            // Determine which lights are selected (if any)
            var selectedLights = effect.SelectedLights;
            bool hasSelection = selectedLights != null && selectedLights.Count > 0;

            for (int i = 0; i < effect.TotalLights; i++)
            {
                if (hasSelection && !selectedLights.Contains(i))
                {
                    _slotColors[slot][i] = "#00000000"; // Transparent for unselected lights
                    continue;
                }

                // Alternate: even=color1, odd=color2, swap on toggle
                bool isEven = (i % 2 == 0);
                if (effect.BlinkOn)
                    _slotColors[slot][i] = isEven ? color1 : color2;
                else
                    _slotColors[slot][i] = isEven ? color2 : color1;
            }
        }

        #endregion

        #region Profile Change Detection

        /// <summary>
        /// Checks if any Color Effects profiles have changed and re-injects containers if needed.
        /// Called every 5s from DataUpdate.
        /// </summary>
        public void CheckProfileChanges()
        {
            var now = DateTime.Now;
            if ((now - _lastProfileCheck).TotalMilliseconds < 5000)
                return;
            _lastProfileCheck = now;

            foreach (var conn in _connections.Values.ToList())
            {
                try
                {
                    if (conn.IsMatrix)
                    {
                        if (conn.MatrixSettings == null) continue;
                        var currentProfile = conn.MatrixSettings.CurrentProfile;
                        if (currentProfile != conn.MatrixProfile)
                        {
                            _log?.Invoke($"[LED] Matrix profile changed for slot {conn.Slot}, re-injecting container");
                            conn.MatrixProfile?.LedContainers?.Remove(conn.MatrixContainer);
                            ReInjectMatrixContainer(conn);
                        }
                    }
                    else
                    {
                        if (conn.LedsSettings == null) continue;
                        var currentProfile = conn.LedsSettings.CurrentProfile;
                        if (currentProfile != conn.LedProfile)
                        {
                            _log?.Invoke($"[LED] LED profile changed for slot {conn.Slot}, re-injecting container");
                            conn.LedProfile?.LedContainers?.Remove(conn.LedContainer);
                            ReInjectLedContainer(conn);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[LED] Error checking profile change for slot {conn.Slot}: {ex.Message}");
                }
            }
        }

        private void ReInjectLedContainer(LedConnection conn)
        {
            var profile = conn.LedsSettings.CurrentProfile;
            if (profile == null) return;

            string jsExpression =
                $"var colors = []; " +
                $"for (var i = 1; i <= {conn.LedCount}; i++) {{ " +
                $"  var c = $prop('{PluginPropertyPrefix}.S{conn.Slot}_Led' + i); " +
                $"  if (c && c.length > 0) colors.push(c); " +
                $"  else colors.push('#00000000'); " +
                $"}} " +
                $"return colors;";

            var container = new LedsScriptedContainer();
            container.ContentFormula = new ExpressionValue(jsExpression, Interpreter.Javascript);
            container.LedCount = conn.LedCount;
            container.Description = ContainerDescription;

            bool wasActive = _activeEffects.ContainsKey(conn.DeviceId);
            container.IsEnabled = wasActive;

            profile.LedContainers.Add(container);

            conn.LedContainer = container;
            conn.LedProfile = profile;
        }

        private void ReInjectMatrixContainer(LedConnection conn)
        {
            var profile = conn.MatrixSettings.CurrentProfile;
            if (profile == null) return;

            string jsExpression =
                $"var colors = []; " +
                $"for (var i = 1; i <= {conn.LedCount}; i++) {{ " +
                $"  var c = $prop('{PluginPropertyPrefix}.S{conn.Slot}_Led' + i); " +
                $"  if (c && c.length > 0) colors.push(c); " +
                $"  else colors.push('#00000000'); " +
                $"}} " +
                $"return colors;";

            var container = new MatrixScriptedContainer();
            container.ContentFormula = new ExpressionValue(jsExpression, Interpreter.Javascript);
            container.Rows = conn.MatrixRows;
            container.Columns = conn.MatrixColumns;
            container.Description = ContainerDescription;

            bool wasActive = _activeEffects.ContainsKey(conn.DeviceId);
            container.IsEnabled = wasActive;

            profile.LedContainers.Add(container);

            conn.MatrixContainer = container;
            conn.MatrixProfile = profile;
        }

        #endregion

        #region Helpers

        private static string GetColorForPriority(LedDeviceConfig config, string priority)
        {
            switch (priority?.ToLower())
            {
                case "urgent": return config.UrgentColor ?? "#FF0000";
                case "vip": return config.VipColor ?? "#FF9800";
                default: return config.NormalColor ?? "#00FF00";
            }
        }

        private static string GetColor2ForPriority(LedDeviceConfig config, string priority)
        {
            switch (priority?.ToLower())
            {
                case "urgent": return config.HueColor2Urgent ?? "#0000FF";
                case "vip": return config.HueColor2Vip ?? "#FFFFFF";
                default: return config.HueColor2Normal ?? "#FFFFFF";
            }
        }

        private static HueEffectType GetHueEffectForPriority(LedDeviceConfig config, string priority)
        {
            switch (priority?.ToLower())
            {
                case "urgent": return config.HueUrgentEffect;
                case "vip": return config.HueVipEffect;
                default: return config.HueNormalEffect;
            }
        }

        private static List<int> GetSelectedLightsForPriority(LedDeviceConfig config, string priority)
        {
            switch (priority?.ToLower())
            {
                case "urgent": return config.SelectedLightsUrgent;
                case "vip": return config.SelectedLightsVip;
                default: return config.SelectedLightsNormal;
            }
        }

        /// <summary>
        /// Converts #RRGGBB to #AARRGGBB format (adds FF alpha).
        /// If already #AARRGGBB, returns as-is.
        /// </summary>
        private static string ToArgbHex(string color)
        {
            if (string.IsNullOrEmpty(color)) return "#FF000000";
            if (color.Length == 9 && color.StartsWith("#")) return color; // Already #AARRGGBB
            if (color.Length == 7 && color.StartsWith("#")) return "#FF" + color.Substring(1); // #RRGGBB -> #FFRRGGBB
            return "#FF000000"; // Fallback to black
        }

        /// <summary>
        /// Gets the slot index for a connected device, or -1 if not connected.
        /// </summary>
        public int GetSlotForDevice(string deviceId)
        {
            foreach (var conn in _connections.Values)
            {
                if (conn.DeviceId == deviceId)
                    return conn.Slot;
            }
            return -1;
        }

        /// <summary>
        /// Returns whether any effect is currently active.
        /// </summary>
        public bool HasActiveEffects
        {
            get
            {
                lock (_effectLock)
                {
                    return _activeEffects.Count > 0;
                }
            }
        }

        #endregion

        public void Dispose()
        {
            StopAllEffects();
            DisconnectAll();
        }

        #region Internal Types

        private class LedConnection
        {
            public string DeviceId;
            public int Slot;
            public int LedCount;
            public bool IsMatrix;
            public int MatrixRows;
            public int MatrixColumns;

            // LED connection
            public LedsScriptedContainer LedContainer;
            public Profile LedProfile;
            public LedsSettings LedsSettings;

            // Matrix connection
            public MatrixScriptedContainer MatrixContainer;
            public RGBMatrixProfile MatrixProfile;
            public MatrixSettings MatrixSettings;
        }

        private class ActiveEffect
        {
            public string DeviceId;
            public int Slot;
            public int LedCount;
            public string Color;
            public string Color2;
            public bool IsMatrix;
            public int MatrixRows;
            public int MatrixColumns;
            public MatrixDisplayMode MatrixMode;
            public bool IsHue;
            public HueEffectType HueEffect;
            public List<int> SelectedLights;
            public int TotalLights;
            public DateTime StartTime;
            public int DurationMs;
            public bool BlinkOn;
            public DateTime NextToggle;
        }

        #endregion
    }

    /// <summary>
    /// Represents a discovered LED device from SimHub.
    /// Used for UI display and connection setup.
    /// </summary>
    public class DiscoveredLedDevice
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public LedDeviceType DeviceType { get; set; }
        public int LedCount { get; set; }
        public int MatrixRows { get; set; }
        public int MatrixColumns { get; set; }

        // References (not serialized, for runtime use only)
        [JsonIgnore]
        public LedModuleDevice LedModuleRef { get; set; }
        [JsonIgnore]
        public AssociatedHueDevice HueDeviceRef { get; set; }
        public List<string> HueLightNames { get; set; } = new List<string>();
    }
}
