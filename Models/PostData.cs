using System;

namespace FacebookAutoPoster.Models
{
    public class PostData
    {
        public string ProfileName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string GroupUrl { get; set; }
        public string PostText { get; set; }
        public bool ClosePreview { get; set; }
        public bool IsAnonymous { get; set; }
    }
} 