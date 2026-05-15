using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;

namespace RaccoonBlog.Web.Infrastructure
{
    public static class SocialPostingSubscription
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string SubscriptionName = "social-posting";

        public const string SocialTag = "social";
        public const string SocialDisableTag = "social-disable";
        public const string SocialRedditTag = "social-reddit";
        public const string SocialTwitterTag = "social-twitter";

        private static bool _started;

        public static void Start(IDocumentStore store)
        {
            if (_started) return;
            _started = true;

            EnsureSubscriptionExists(store);
            var worker = store.Subscriptions.GetSubscriptionWorker<Post>(SubscriptionName);

            worker.AfterAcknowledgment += _ =>
            {
                _log.Info("Social posting subscription batch acknowledged.");
                return Task.CompletedTask;
            };

            Task.Run(async () =>
            {
                try
                {
                    await worker.Run(batch =>
                    {
                        foreach (var item in batch.Items)
                        {
                            ProcessPost(store, item.Result);
                        }
                        return Task.CompletedTask;
                    });
                }
                catch (Exception e)
                {
                    _log.Fatal(e, "Social posting subscription worker failed");
                }
            });
        }

        private static void ProcessPost(IDocumentStore store, Post post)
        {
            if (post.PublishAt > DateTimeOffset.UtcNow)
            {
                // Set @refresh so we get triggered when the post goes live
                using var session = store.OpenSession();
                var metadata = session.Advanced.GetMetadataFor(session.Load<Post>(post.Id));
                if (metadata != null && !metadata.ContainsKey("@refresh"))
                {
                    metadata["@refresh"] = post.PublishAt.ToString("o");
                    session.SaveChanges();
                }
                return;
            }

            var tags = (post.TagsAsSlugs ?? Enumerable.Empty<string>()).ToList();

            bool hasSocialAll = tags.Contains(SocialTag);
            bool hasReddit = hasSocialAll || tags.Contains(SocialRedditTag);
            bool hasTwitter = hasSocialAll || tags.Contains(SocialTwitterTag);

            if (!hasReddit && !hasTwitter)
                return;

            if (tags.Contains(SocialDisableTag))
                return;

            if (hasReddit)
                TryPostToReddit(store, post);

            if (hasTwitter)
                TryPostToTwitter(store, post);
        }

        private static void TryPostToReddit(IDocumentStore store, Post post)
        {
            if (post.Integration?.Reddit?.Submitted == true)
                return;

            // TODO: Reddit integration is currently disabled in .NET 8 migration
            // (SubmitToRedditStrategy is wrapped in #if FALSE).
            // Re-enable when RedditSharp 2.0+ API is integrated.
            _log.Warn("Reddit posting disabled (pending RedditSharp 2.0 migration) for post {PostId}", post.Id);
        }

        private static void TryPostToTwitter(IDocumentStore store, Post post)
        {
            try
            {
                using var session = store.OpenSession();
                var blogConfig = session.Load<BlogConfig>("Blog/Config");

                if (string.IsNullOrEmpty(blogConfig?.TwitterBearerToken))
                {
                    _log.Info("Twitter bearer token not configured, skipping post {PostId}", post.Id);
                    return;
                }

                var postUrl = PostHelper.Url(post);
                var tweetText = post.Social.TwitterText + " " + postUrl;

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", blogConfig.TwitterBearerToken);

                var content = new System.Net.Http.StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { text = tweetText }),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = client.PostAsync("https://api.x.com/2/tweets", content).Result;

                if (response.IsSuccessStatusCode)
                    _log.Info("Tweet posted for {PostId}", post.Id);
                else
                    _log.Warn("Twitter API error for {PostId}: {Status}", post.Id, response.StatusCode);
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to post tweet for {PostId}", post.Id);
            }
        }

        private static void EnsureSubscriptionExists(IDocumentStore store)
        {
            try
            {
                store.Subscriptions.GetSubscriptionState(SubscriptionName);
            }
            catch (SubscriptionDoesNotExistException)
            {
                store.Subscriptions.Create(new SubscriptionCreationOptions
                {
                    Name = SubscriptionName,
                    Query = "from Posts where Social.GeneratedAt != null and (Social.DisableAutoPublish == false or Social.DisableAutoPublish == null) and not exists(@metadata.@refresh)",
                    ChangeVector = "LastDocument"
                });
                _log.Info("Created data subscription '{Name}'.", SubscriptionName);
            }
        }
    }
}
