using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using RaccoonBlog.Web.ViewModels;

namespace RaccoonBlog.Web.Helpers
{
    public static class SeoHelper
    {
        public static IHtmlString RenderJsonLd(PostViewModel.PostDetails post, string canonicalUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<script type=\"application/ld+json\">");
            sb.AppendLine("{");
            sb.AppendLine("  \"@context\": \"https://schema.org\",");
            sb.AppendLine("  \"@type\": \"BlogPosting\",");
            sb.AppendLine("  \"headline\": " + HttpUtility.JavaScriptStringEncode(post.Title) + ",");
            sb.AppendLine("  \"url\": " + HttpUtility.JavaScriptStringEncode(canonicalUrl) + ",");

            if (post.PublishedAt != default)
                sb.AppendLine("  \"datePublished\": \"" + post.PublishedAt.ToString("yyyy-MM-ddTHH:mm:ssK") + "\",");

            if (post.CreatedAt != default)
                sb.AppendLine("  \"dateCreated\": \"" + post.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssK") + "\",");

            var description = post.SeoMetaDescription ?? "";
            if (string.IsNullOrEmpty(description) == false)
                sb.AppendLine("  \"description\": " + HttpUtility.JavaScriptStringEncode(description) + ",");

            if (post.Author != null)
            {
                sb.AppendLine("  \"author\": {");
                sb.AppendLine("    \"@type\": \"Person\",");
                sb.AppendLine("    \"name\": " + HttpUtility.JavaScriptStringEncode(post.Author.FullName ?? "Oren Eini") + "");
                sb.AppendLine("  },");
            }

            if (post.Tags != null && post.Tags.Any())
            {
                sb.Append("  \"keywords\": \"");
                sb.Append(string.Join(", ", post.Tags.Select(t => t.Name)));
                sb.AppendLine("\",");
            }

            if (post.SeoKeywords != null && post.SeoKeywords.Any())
            {
                sb.Append("  \"keywords\": \"");
                sb.Append(string.Join(", ", post.SeoKeywords));
                sb.AppendLine("\"");
            }
            else if (post.Tags == null || post.Tags.Any() == false)
            {
                sb.Length -= 2;
                sb.AppendLine();
            }
            else
            {
                sb.Length -= 2;
                sb.AppendLine();
            }

            sb.AppendLine("}");
            sb.AppendLine("</script>");
            return new HtmlString(sb.ToString());
        }

        public static string GetEffectiveMetaDescription(PostViewModel.PostDetails post)
        {
            if (string.IsNullOrWhiteSpace(post.SeoMetaDescription) == false)
                return post.SeoMetaDescription;

            return PostHelper.GetMetaDescription(post.Body);
        }
    }
}
