using System;

namespace WhatsAppSimHubPlugin.Models
{
    public class QueuedMessage
    {
        public string Id { get; set; }
        public string From { get; set; }
        public string Number { get; set; }
        public string Body { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsVip { get; set; }
        public bool IsUrgent { get; set; }
        public bool WasDisplayed { get; set; }
        public DateTime? LastDisplayed { get; set; }
        public int DisplayCount { get; set; }
        public string ChatId { get; set; }

        public QueuedMessage()
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.Now;
            WasDisplayed = false;
            DisplayCount = 0;
        }


    }
}
