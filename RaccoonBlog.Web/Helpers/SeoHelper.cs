using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using RaccoonBlog.Web.ViewModels;

namespace RaccoonBlog.Web.Helpers
{
    public static class SeoHelper
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        public static IHtmlString RenderJsonLd(PostViewModel.PostDetails post, string canonicalUrl)
        {
            var keywords = new List<string>();
            if (post.SeoKeywords != null && post.SeoKeywords.Any())
                keywords.AddRange(post.SeoKeywords);
            else if (post.Tags != null && post.Tags.Any())
                keywords.AddRange(post.Tags.Select(t => t.Name));

            var schema = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "BlogPosting",
                ["headline"] = post.Title,
                ["url"] = canonicalUrl,
            };

            if (post.PublishedAt != default)
                schema["datePublished"] = post.PublishedAt.ToString("yyyy-MM-ddTHH:mm:ssK");

            if (post.CreatedAt != default)
                schema["dateCreated"] = post.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssK");

            if (string.IsNullOrWhiteSpace(post.SeoMetaDescription) == false)
                schema["description"] = post.SeoMetaDescription;

            if (post.Author != null)
            {
                schema["author"] = new Dictionary<string, object>
                {
                    ["@type"] = "Person",
                    ["name"] = post.Author.FullName
                };
            }

            if (keywords.Any())
                schema["keywords"] = string.Join(", ", keywords);

            var json = JsonConvert.SerializeObject(schema, JsonSettings);
            return new HtmlString("<script type=\"application/ld+json\">" + json + "</script>");
        }
    }
}
