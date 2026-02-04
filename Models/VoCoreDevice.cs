namespace WhatsAppSimHubPlugin.Models
{
    /// <summary>
    /// Represents a VoCore device with its current state
    /// </summary>
    public class VoCoreDevice
    {
        public string Name { get; set; }
        public string Serial { get; set; }
        public bool InformationOverlayEnabled { get; set; }
        public string CurrentDashboard { get; set; }
    }
}
