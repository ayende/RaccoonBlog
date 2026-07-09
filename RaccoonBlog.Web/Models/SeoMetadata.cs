using System;
using System.Collections.Generic;

namespace RaccoonBlog.Web.Models
{
    public class SeoMetadata
    {
        public string MetaDescription { get; set; }
        public ICollection<string> Keywords { get; set; }
        public DateTimeOffset? LastAnalyzedAt { get; set; }
    }
}
