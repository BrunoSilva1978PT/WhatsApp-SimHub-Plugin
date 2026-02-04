using System.Collections.Generic;

namespace WhatsAppSimHubPlugin.Models
{
    public class PluginSettings
    {
        // Backend Mode
        public string BackendMode { get; set; } = "whatsapp-web.js"; // "whatsapp-web.js" ou "baileys"

        // Backend Library Sources (Official or Manual)
        public string WhatsAppWebJsSource { get; set; } = "official"; // "official" or "manual"
        public string WhatsAppWebJsManualRepo { get; set; } = ""; // e.g., "user/fork#branch"
        public string WhatsAppWebJsVersion { get; set; } = ""; // Current installed version

        public string BaileysSource { get; set; } = "official"; // "official" or "manual"
        public string BaileysManualRepo { get; set; } = ""; // e.g., "user/fork#branch"
        public string BaileysVersion { get; set; } = ""; // Current installed version

        // Display
        public bool VoCoreEnabled { get; set; } = true; // Enable/disable VoCore output (disable for VR-only users)
        public string TargetDevice { get; set; } = "";
        public string TargetDeviceName { get; set; } = ""; // Nome do dispositivo (guardado para mostrar quando offline)

        // Queue
        public int MaxGroupSize { get; set; } = 5; // Max messages per contact in queue
        public int MaxQueueSize { get; set; } = 10;
        public int NormalDuration { get; set; } = 5000;
        public int UrgentDuration { get; set; } = 10000;

        // VIP/Urgent Behavior
        public bool RemoveAfterFirstDisplay { get; set; } = false; // Se true, VIP/URGENT remove após 1ª exibição
        public int ReminderInterval { get; set; } = 180000; // Intervalo entre repetições VIP/Urgent (ms) - Default 3 min

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
            // Construtor vazio - keywords default são adicionadas via EnsureDefaults()
        }

        public void EnsureDefaults()
        {
            // Adicionar keywords default só se a lista estiver completamente vazia
            if (Keywords == null || Keywords.Count == 0)
            {
                Keywords = new List<string> { "urgent", "emergency", "hospital", "help" };
            }
        }
    }
}
