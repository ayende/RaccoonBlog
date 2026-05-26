using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RaccoonBlog.IntegrationTests.Infrastructure;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents.Session;
using Xunit;

namespace RaccoonBlog.IntegrationTests.Web.Controllers
{
    public class PostDetailsImageUrlTests : RaccoonControllerTests
    {
        public PostDetailsImageUrlTests(TestWebApplicationFactory factory) : base(factory)
        {
            _ = factory.Services;
        }

        [Fact]
        public async Task WhenPostHasWrongImage_ImageIsNowCorrect()
        {
            SeedPost("posts/1", "<img src=\"http://ayende.com/Blog/images/ayende_com/Blog/WindowsLiveWriter/BroadSupportindeed_A6E/image_thumb.png\" />");

            Assert.Contains("http://ayende.com/Blog/images/", GetStoredBody("posts/1"));

            var html = await RenderPost("posts/1");
            
            Assert.Contains("/blog/Images/ayende_com/Blog/WindowsLiveWriter/BroadSupportindeed_A6E/image_thumb.png", html);
            Assert.DoesNotContain("http://ayende.com/Blog/images/", html);
        }

        [Fact]
        public async Task WhenPostHasMissingImage_ImageIsNowPresent()
        {
            SeedPost("posts/2", "<img src=\"Images/screenshot.png\" />");

            Assert.DoesNotContain("/blog/", GetStoredBody("posts/2"));

            var html = await RenderPost("posts/2");
            
            Assert.Contains("/blog/Images/screenshot.png", html);
        }

        [Fact]
        public async Task WhenPostHasGoodImage_ImageIsStillGood()
        {
            SeedPost("posts/3", "<img src=\"/blog/Images/screenshot.png\" />");

            Assert.Contains("/blog/Images/screenshot.png", GetStoredBody("posts/3"));

            var html = await RenderPost("posts/3");
            
            Assert.Contains("/blog/Images/screenshot.png", html);
            Assert.DoesNotContain("/blog/Images//blog/", html);
        }

        private void SeedPost(string postId, string body)
        {
            var urlId = Post.GetIdForUrl(postId);
            SetupData(session =>
            {
                session.Store(new BlogConfig { Id = "Blog/Config", Title = "Test Blog" });
                session.Store(new User { Id = "users/1", FullName = "Test Author", Email = "author@test.com" });
                session.Store(new PostComments
                {
                    Id = $"posts/comments/{urlId}",
                    Post = new PostComments.PostReference { Id = postId, PublishAt = DateTimeOffset.Now.AddDays(-1) }
                });
                session.Store(new Post
                {
                    Id = postId,
                    Title = "Test Post",
                    Body = body,
                    PublishAt = DateTimeOffset.Now.AddDays(-1),
                    CreatedAt = DateTimeOffset.Now.AddDays(-1),
                    AuthorId = "users/1",
                    CommentsId = $"posts/comments/{urlId}",
                    AllowComments = false,
                    Tags = new List<string>()
                });
            });
        }

        private string GetStoredBody(string postId)
        {
            string body = null;
            ExecuteWithScope(services =>
            {
                body = services.GetRequiredService<IDocumentSession>().Load<Post>(postId).Body;
            });
            return body;
        }

        private async Task<string> RenderPost(string postId)
        {
            var urlId = Post.GetIdForUrl(postId);
            var response = await Factory
                .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true })
                .GetAsync($"/{urlId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
    }
}
