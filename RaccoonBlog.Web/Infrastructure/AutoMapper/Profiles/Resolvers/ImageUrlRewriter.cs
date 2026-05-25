using System;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;

public static class ImageUrlRewriter
{
    private static readonly Regex ImageUrlRegex = new(@"^https?://(?:www\.)?ayende\.com/blog/images/(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static string RewriteImageUrls(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        if (!html.Contains("<img", StringComparison.OrdinalIgnoreCase))
            return html;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes == null)
            return html;

        var changed = false;
        foreach (var img in imgNodes)
        {
            var src = img.GetAttributeValue("src", "");
            var newSrc = RewriteSrc(src);
            if (newSrc == src)
                continue;

            img.SetAttributeValue("src", newSrc);
            changed = true;
        }

        return changed ? doc.DocumentNode.OuterHtml : html;
    }

    private static string RewriteSrc(string src)
    {
        if (string.IsNullOrEmpty(src) || src.Contains(".."))
            return src;

        if (src.StartsWith("/blog/Images/", StringComparison.OrdinalIgnoreCase))
            return src;
        
        var match = ImageUrlRegex.Match(src);
        if (match.Success)
            return "/blog/Images/" + match.Groups[1].Value;
        
        if (src.StartsWith("/Images/", StringComparison.OrdinalIgnoreCase))
            return "/blog/Images/" + src.Substring("/Images/".Length);
        
        if (src.StartsWith("Images/", StringComparison.OrdinalIgnoreCase))
            return "/blog/Images/" + src.Substring("Images/".Length);

        return src;
    }
}
