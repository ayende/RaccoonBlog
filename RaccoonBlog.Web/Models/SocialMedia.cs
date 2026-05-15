using System;

namespace RaccoonBlog.Web.Models
{
    public class SocialMedia
    {
        public string TwitterText { get; set; }
        public string RedditTitle { get; set; }
        public DateTimeOffset? GeneratedAt { get; set; }
        public bool DisableAutoPublish { get; set; }
    }
}
