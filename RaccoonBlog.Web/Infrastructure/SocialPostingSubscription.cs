using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.Services;

namespace RaccoonBlog.Web.Infrastructure
{
    /// <summary>
    /// RavenDB subscription worker that handles social media posting for blog posts.
    /// Replaces the FluentScheduler polling approach with a reactive subscription.
    /// Posts are submitted when they are published, have AI-generated social text, and are tagged
    /// with "social". The old "reddit" tag is also honored for backward compatibility.
    /// Future posts are handled via @refresh metadata set by the social GenAI UpdateScript.
    /// </summary>
    public static class SocialPostingSubscription
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        public const string SocialTag = "social";
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;

            var store = MvcApplication.DocumentStore;
            // Expects a data subscription named "social-posting" on the Posts collection
            // to be created in RavenDB Studio before starting the application.
            var worker = store.Subscriptions.GetSubscriptionWorker<Post>("social-posting");

            worker.AfterAcknowledgment += (_, _) => _log.Info("Social posting subscription batch acknowledged.");

            Task.Run(async () =>
            {
                try
                {
                    await worker.Run(async batch =>
                    {
                        foreach (var item in batch.Items)
                        {
                            ProcessPost(item.Result);
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

        private static void ProcessPost(Post post)
        {
            if (post.PublishAt > DateTimeOffset.UtcNow)
                return; // not published yet — @refresh will trigger us at PublishAt

            if (post.Social?.GeneratedAt == null)
                return; // AI text not ready yet — will retry when GenAI task updates the post

            if (post.Social.DisableAutoPublish)
                return;

            var tags = post.TagsAsSlugs ?? new List<string>();
            if (!tags.Contains(SocialTag))
                return;

            TryPostToReddit(post);
            TryPostToTwitter(post);
        }

        private static void TryPostToReddit(Post post)
        {
            if (post.Integration?.Reddit?.Submitted == true)
                return; // already submitted to all subreddits

            try
            {
                using (var session = MvcApplication.DocumentStore.OpenSession())
                {
                    // Reload in session context for proper change tracking
                    var sessionPost = session
                        .Include<Post>(x => x.AuthorId)
                        .Load(post.Id);

                    if (sessionPost == null) return;

                    var strategy = new SubmitToRedditStrategy(session);
                    strategy.SubmitPosts(new List<Post> { sessionPost });
                    session.SaveChanges();
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Failed to submit post {PostId} to Reddit via subscription", post.Id);
            }
        }

        private static void TryPostToTwitter(Post post)
        {
            // TODO: Implement Twitter/X API integration.
            // Requires API credentials in BlogConfig or AppSettings.
            // Should post: post.Social.TwitterText + " " + PostHelper.Url(post)
            // Track status on post.Integration.Twitter (mirror Reddit pattern).
            _log.Info("Twitter posting not yet implemented for post {PostId}", post.Id);
        }
    }
}
