using System;

namespace FacebookAutoPoster.Models
{
    public class PostData
    {
        public string ProfileName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string GroupUrl { get; set; } = string.Empty;
        public string PostText { get; set; } = string.Empty;
        public bool ClosePreview { get; set; }
        public bool IsAnonymous { get; set; }
    }
} 