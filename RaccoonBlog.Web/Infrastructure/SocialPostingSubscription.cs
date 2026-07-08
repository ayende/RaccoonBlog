using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Infrastructure.Common;
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
        public const string SocialTwitterTag = "social-twitter";

        private static bool _started;

        public static void Start(IDocumentStore store)
        {
            if (_started) return;
            _started = true;

            // Run all RavenDB interaction (subscription creation + the worker loop) on a
            // background task so app startup never blocks on — or crashes from — RavenDB being
            // unreachable. On any failure (including RavenDB being down at startup) it retries
            // after a delay, so the worker self-heals once the server becomes reachable.
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        EnsureSubscriptionExists(store);
                        var worker = store.Subscriptions.GetSubscriptionWorker<Post>(SubscriptionName);

                        worker.AfterAcknowledgment += _ =>
                        {
                            _log.Info("Social posting subscription batch acknowledged.");
                            return Task.CompletedTask;
                        };

                        await worker.Run(async batch =>
                        {
                            foreach (var item in batch.Items)
                            {
                                await ProcessPost(store, item.Result);
                            }
                        });
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, "Social posting subscription worker error; retrying in 30s");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            });
        }

        private static async Task ProcessPost(IDocumentStore store, Post post)
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

            bool hasTwitter = tags.Contains(SocialTag) || tags.Contains(SocialTwitterTag);

            if (!hasTwitter)
                return;

            if (tags.Contains(SocialDisableTag))
                return;

            await TryPostToTwitter(store, post);
        }

        private static void EnqueueSocialFailureEmail(IDocumentStore store, Post post, string network, string target, string errorDetail)
        {
            try
            {
                using var session = store.OpenSession();
                var cmd = new SendEmailCommand
                {
                    Type = "SocialPostingFailure",
                    Subject = $"Social posting to {network} failed: {post.Title}",
                    Network = network,
                    Target = target ?? "",
                    ErrorMessage = errorDetail,
                    PostId = post.Id,
                    PostTitle = post.Title,
                    PostSlug = SlugConverter.TitleToSlug(post.Title),
                    CreatedAt = DateTimeOffset.Now
                };
                session.Store(cmd, "EmailCommands/social-failure-" + Guid.NewGuid().ToString("N"));
                session.SaveChanges();
                _log.Info("Queued {Network} failure email for {PostId}", network, post.Id);
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to queue {Network} failure email for {PostId}", network, post.Id);
            }
        }

        private static async Task TryPostToTwitter(IDocumentStore store, Post post)
        {
            try
            {
                if (string.IsNullOrEmpty(post.Social?.TwitterText))
                {
                    _log.Info("No Twitter text generated, skipping post {PostId}", post.Id);
                    return;
                }

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

                var response = await client.PostAsync("https://api.x.com/2/tweets", content);

                if (response.IsSuccessStatusCode)
                {
                    _log.Info("Tweet posted for {PostId}", post.Id);
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var detail = $"Twitter API returned {(int)response.StatusCode} {response.StatusCode}.\nResponse body:\n{body}";
                    _log.Warn("Twitter API error for {PostId}: {Status}", post.Id, response.StatusCode);
                    EnqueueSocialFailureEmail(store, post, "Twitter", "", detail);
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to post tweet for {PostId}", post.Id);
                EnqueueSocialFailureEmail(store, post, "Twitter", "", e.ToString());
            }
        }

        private static void EnsureSubscriptionExists(IDocumentStore store)
        {
            try
            {
                if (store.Subscriptions.GetSubscriptionState(SubscriptionName) != null)
                    return;
            }
            catch (SubscriptionDoesNotExistException)
            {
            }

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
