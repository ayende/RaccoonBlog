using RaccoonBlog.Web.Infrastructure.AutoMapper.Profiles.Resolvers;
using Xunit;

namespace RaccoonBlog.IntegrationTests
{
    public class ImageUrlRewriterTests
    {
        private static string Rewrite(string html) => ImageUrlRewriter.RewriteImageUrls(html);

        [Fact]
        public void WhenImageHasAbsolutePath_UrlIsRewritten()
        {
            var result = Rewrite("<img src=\"/Images/foo.png\" />");

            Assert.Contains("/blog/Images/foo.png", result);
        }

        [Fact]
        public void WhenImageHasExternalUrl_UrlIsNotChanged()
        {
            var result = Rewrite("<img src=\"http://lostechies.com/gabrielschenker/files/2011/08/image_thumb.png\" />");

            Assert.Contains("http://lostechies.com/gabrielschenker/files/2011/08/image_thumb.png", result);
        }

        [Fact]
        public void WhenPostHasMultipleImages_AllUrlsAreRewritten()
        {
            var result = Rewrite(
                "<img src=\"http://ayende.com/Blog/images/ayende_com/Blog/WindowsLiveWriter/BroadSupportindeed_A6E/image_thumb.png\" />" +
                "<img src=\"Images/screenshot.png\" />");

            Assert.Contains("/blog/Images/ayende_com/Blog/WindowsLiveWriter/BroadSupportindeed_A6E/image_thumb.png", result);
            Assert.Contains("/blog/Images/screenshot.png", result);
        }
    }
}
