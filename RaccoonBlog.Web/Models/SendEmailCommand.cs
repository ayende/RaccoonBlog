using System;
using System.Collections.Generic;

namespace RaccoonBlog.Web.Models
{
    public class SendEmailCommand
    {
        public string Type { get; set; }
        public string ReplyTo { get; set; }
        public string Subject { get; set; }
        public string View { get; set; }
        public string SendTo { get; set; }

        public string ModelJson { get; set; }

        public int CommentId { get; set; }
        public string Author { get; set; }
        public string CommentBody { get; set; }
        public string CommentEmail { get; set; }
        public string CommentUrl { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
        public string CommenterId { get; set; }

        public string PostId { get; set; }
        public string PostTitle { get; set; }
        public string PostSlug { get; set; }
        public string BlogName { get; set; }
        public string Key { get; set; }

        public string DigestDate { get; set; }
        public List<SpamDigestEntry> SpamComments { get; set; }
    }

    public class SpamDigestEntry
    {
        public int CommentId { get; set; }
        public string Author { get; set; }
        public string Body { get; set; }
        public string PostId { get; set; }
        public string PostTitle { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
