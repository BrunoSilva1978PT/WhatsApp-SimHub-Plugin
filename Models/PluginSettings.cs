using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace WhatsAppSimHubPlugin.Models
{
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class PluginSettings
    {
        // Backend Mode
        public string BackendMode { get; set; } = "whatsapp-web.js"; // "whatsapp-web.js" or "baileys"

        // Backend Library Sources (Official or Manual)
        public string WhatsAppWebJsSource { get; set; } = "official"; // "official" or "manual"
        public string WhatsAppWebJsManualRepo { get; set; } = ""; // e.g., "user/fork#branch"
        public string WhatsAppWebJsVersion { get; set; } = ""; // Current installed version

        public string BaileysSource { get; set; } = "official"; // "official" or "manual"
        public string BaileysManualRepo { get; set; } = ""; // e.g., "user/fork#branch"
        public string BaileysVersion { get; set; } = ""; // Current installed version

        // Display
        public bool VoCoreEnabled { get; set; } = true; // Enable/disable VoCore output (disable for VR-only users)

        // VoCore Devices (support up to 2)
        public string VoCore1_Name { get; set; } = "";
        public string VoCore1_Serial { get; set; } = "";
        public string VoCore2_Name { get; set; } = "";
        public string VoCore2_Serial { get; set; } = "";

        // Dashboard Configuration per VoCore
        // LayerCount: 1 = use Layer1 directly, 2 = merge Layer1 + Layer2
        public int VoCore1_LayerCount { get; set; } = 1;
        public string VoCore1_Layer1 { get; set; } = "WhatsAppPluginVocore1";
        public string VoCore1_Layer2 { get; set; } = "";

        public int VoCore2_LayerCount { get; set; } = 1;
        public string VoCore2_Layer1 { get; set; } = "WhatsAppPluginVocore2";
        public string VoCore2_Layer2 { get; set; } = "";

        // Queue
        public int MaxGroupSize { get; set; } = 5; // Max messages per contact in queue
        public int MaxQueueSize { get; set; } = 10;
        public int NormalDuration { get; set; } = 5000;
        public int UrgentDuration { get; set; } = 10000;

        // VIP/Urgent Behavior
        public bool RemoveAfterFirstDisplay { get; set; } = false; // If true, VIP/URGENT removes after 1st display
        public int ReminderInterval { get; set; } = 180000; // Interval between VIP/Urgent repetitions (ms) - Default 3 min

        // Sound Notifications
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool SoundEnabled { get; set; } = true;
        public string VipSoundFile { get; set; } = "mixkit-bell-notification-933.wav";
        public string UrgentSoundFile { get; set; } = "mixkit-urgent-simple-tone-loop-2976.wav";

        // Dependencies
        public bool DependenciesInstalling { get; set; } = false; // True when installing dependencies in background

        // Quick Replies
        public string Reply1Text { get; set; } = "I'm in a race, will call you later 🏎️";
        public string Reply2Text { get; set; } = "If it's urgent please call me 📞";
        public bool ShowConfirmation { get; set; } = true;

        // Data
        public List<Contact> Contacts { get; set; } = new List<Contact>();
        public List<string> Keywords { get; set; } = new List<string>();

        public PluginSettings()
        {
            // Empty constructor - default keywords are added via EnsureDefaults()
        }

        public void EnsureDefaults()
        {
            // Add default keywords only if list is completely empty
            if (Keywords == null || Keywords.Count == 0)
            {
                Keywords = new List<string> { "urgent", "emergency", "hospital", "help" };
            }
        }
    }
}
