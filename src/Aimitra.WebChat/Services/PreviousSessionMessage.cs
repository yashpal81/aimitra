using System;

namespace Aimitra.WebChat.Services
{
    public class PreviousSessionMessage
    {
        public string MessageId { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public bool IsPartial { get; set; }
    }
}
