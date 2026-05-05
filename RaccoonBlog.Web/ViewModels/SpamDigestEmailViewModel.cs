using System;
using System.Collections.Generic;
using System.Web.Mvc;

namespace RaccoonBlog.Web.ViewModels
{
    public class SpamDigestEmailViewModel
    {
        public string BlogName { get; set; }
        public string Date { get; set; }
        public int TotalCount { get; set; }
        public List<SpamCommentViewModel> Comments { get; set; }

        public class SpamCommentViewModel
        {
            public int CommentId { get; set; }
            public string Author { get; set; }
            public string Body { get; set; }
            public string BodyPreview { get; set; }
            public string PostId { get; set; }
            public string PostTitle { get; set; }
            public string PostSlug { get; set; }
            public DateTimeOffset Timestamp { get; set; }
        }
    }
}
