using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WhatsAppSimHubPlugin.Models
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LedDeviceType
    {
        LedDevice,
        DeviceMatrix,
        ArduinoLeds,
        ArduinoMatrix,
        PhilipsHue
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MatrixDisplayMode
    {
        FlashAll,
        EnvelopeIcon
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum HueEffectType
    {
        Flash,
        Alternating
    }

    public class LedDeviceConfig
    {
        // Identification
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public LedDeviceType DeviceType { get; set; } = LedDeviceType.LedDevice;

        public bool Enabled { get; set; } = false;

        // LED count info (for display)
        public int LedCount { get; set; } = 0;
        public int MatrixRows { get; set; } = 0;
        public int MatrixColumns { get; set; } = 0;

        // Flash colors per priority (#RRGGBB format)
        public string NormalColor { get; set; } = "#00FF00";
        public string VipColor { get; set; } = "#FF9800";
        public string UrgentColor { get; set; } = "#FF0000";

        // Matrix-specific: display mode
        [JsonConverter(typeof(StringEnumConverter))]
        public MatrixDisplayMode MatrixMode { get; set; } = MatrixDisplayMode.FlashAll;

        // Hue-specific: effect type per priority
        [JsonConverter(typeof(StringEnumConverter))]
        public HueEffectType HueNormalEffect { get; set; } = HueEffectType.Flash;
        [JsonConverter(typeof(StringEnumConverter))]
        public HueEffectType HueVipEffect { get; set; } = HueEffectType.Alternating;
        [JsonConverter(typeof(StringEnumConverter))]
        public HueEffectType HueUrgentEffect { get; set; } = HueEffectType.Alternating;

        // Hue-specific: second color for alternating effect (#RRGGBB)
        public string HueColor2Normal { get; set; } = "#FFFFFF";
        public string HueColor2Vip { get; set; } = "#FFFFFF";
        public string HueColor2Urgent { get; set; } = "#0000FF";

        // Hue-specific: selected light indices per priority
        public List<int> SelectedLightsNormal { get; set; } = new List<int>();
        public List<int> SelectedLightsVip { get; set; } = new List<int>();
        public List<int> SelectedLightsUrgent { get; set; } = new List<int>();

        // Legacy: global selection (kept for backward compatibility on load)
        public List<int> SelectedLights { get; set; } = new List<int>();

        /// <summary>
        /// Migrates old global SelectedLights to per-priority lists if needed.
        /// </summary>
        public void MigrateSelectedLights()
        {
            if (SelectedLights != null && SelectedLights.Count > 0)
            {
                if (SelectedLightsNormal.Count == 0)
                    SelectedLightsNormal = new List<int>(SelectedLights);
                if (SelectedLightsVip.Count == 0)
                    SelectedLightsVip = new List<int>(SelectedLights);
                if (SelectedLightsUrgent.Count == 0)
                    SelectedLightsUrgent = new List<int>(SelectedLights);
                SelectedLights.Clear();
            }
        }
    }
}
