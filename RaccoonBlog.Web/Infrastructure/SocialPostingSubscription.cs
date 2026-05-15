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
                    await worker.Run(async batch =>
                    {
                        foreach (var item in batch.Items)
                        {
                            ProcessPost(store, item.Result);
                        }

                        await Task.CompletedTask;
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
                return;

            if (post.Social?.GeneratedAt == null)
                return;

            if (post.Social.DisableAutoPublish)
                return;

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
                TryPostToTwitter(post);
        }

        private static void TryPostToReddit(IDocumentStore store, Post post)
        {
            if (post.Integration?.Reddit?.Submitted == true)
                return;

            // TODO: Reddit integration is currently disabled in .NET 8 migration
            // (SubmitToRedditStrategy is wrapped in #if FALSE).
            // Re-enable when RedditSharp 2.0+ API is integrated.
            _log.Info("Reddit posting pending RedditSharp migration for post {PostId}", post.Id);
        }

        private static void TryPostToTwitter(Post post)
        {
            // TODO: Implement Twitter/X API integration.
            _log.Info("Twitter posting not yet implemented for post {PostId}", post.Id);
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
                    Query = "from Posts where Social.GeneratedAt != null",
                    ChangeVector = "LastDocument"
                });
                _log.Info("Created data subscription '{Name}'.", SubscriptionName);
            }
        }
    }
}
