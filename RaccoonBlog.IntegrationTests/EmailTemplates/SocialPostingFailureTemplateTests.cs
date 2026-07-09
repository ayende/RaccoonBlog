using System;
using System.IO;
using RaccoonBlog.Web.Models;
using Scriban;
using Xunit;

namespace RaccoonBlog.IntegrationTests.EmailTemplates
{
    public class SocialPostingFailureTemplateTests
    {
        [Fact]
        public void Template_Embeds_Parses_And_Renders_Fields()
        {
            var asm = typeof(SendEmailCommand).Assembly;
            var resourceName = Array.Find(
                asm.GetManifestResourceNames(),
                n => n.EndsWith("EmailTemplates.SocialPostingFailure.html", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(resourceName);

            using var stream = asm.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            var templateText = reader.ReadToEnd();

            var template = Template.Parse(templateText);
            Assert.False(template.HasErrors, string.Join("; ", template.Messages));

            var html = template.Render(new
            {
                blog_name = "Test Blog",
                network = "Twitter",
                target = "https://x.com",
                post_title = "My Post",
                post_id = "posts/1",
                post_slug = "my-post",
                error_message = "System.Exception: boom <fail>"
            });

            Assert.Contains("Twitter", html);
            Assert.Contains("https://x.com", html);
            Assert.Contains("My Post", html);
            Assert.Contains("boom", html);
            // error detail must be HTML-encoded (no raw angle brackets from the trace)
            Assert.Contains("&lt;fail&gt;", html);
        }
    }
}
