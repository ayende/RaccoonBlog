using System;

namespace RaccoonBlog.Web.Models
{
    public class SocialPublishCommand
    {
        public string Id { get; set; }
        public string PostId { get; set; }
        public string Target { get; set; }
        public string Account { get; set; }
        public DateTimeOffset PublishAt { get; set; }
        public CommandStatus Status { get; set; }
        public string TweetId { get; set; }
        public int Attempts { get; set; }
        public string ErrorMessage { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    public enum CommandStatus
    {
        Pending,
        Completed,
        Failed
    }
}
