using System.Collections.Generic;

namespace WhatsAppSimHubPlugin.Models
{
    public class PluginSettings
    {
        // Display
        public string TargetDevice { get; set; } = "";

        // Grouping
        public bool EnableGrouping { get; set; } = true;
        public int MaxGroupSize { get; set; } = 5;
        public int GroupWaitTime { get; set; } = 10000; // ms
        public int GroupDuration { get; set; } = 10000;

        // Queue
        public int MaxQueueSize { get; set; } = 10;
        public int NormalDuration { get; set; } = 5000;
        public int UrgentDuration { get; set; } = 10000;

        // Reminders
        public bool RemindVip { get; set; } = true;
        public bool RemindUrgent { get; set; } = true;
        public bool RemoveAfterFirstDisplay { get; set; } = false; // Se true, VIP/URGENT remove após 1ª exibição
        // NOTA: ReminderInterval removido - lógica agora é "1 reply por mensagem visível"

        // Quick Replies
        public string Reply1Text { get; set; } = "Estou numa corrida, ligo depois 🏎️";
        public string Reply2Text { get; set; } = "Se for urgente liga sff 📞";

        public bool RemoveAfterReply { get; set; } = true;
        public bool ShowConfirmation { get; set; } = true;
        // NOTA: EnableCooldown removido - botão bloqueia após 1 envio até mensagem desaparecer

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
                Keywords = new List<string> { "urgente", "emergência", "hospital", "ajuda" };
            }
        }
    }
}
