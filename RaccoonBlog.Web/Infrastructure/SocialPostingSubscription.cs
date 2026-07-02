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
                            await ProcessPost(store, item.Result);
                        }
                    });
                }
                catch (Exception e)
                {
                    _log.Fatal(e, "Social posting subscription worker failed");
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

            bool hasSocialAll = tags.Contains(SocialTag);
            bool hasReddit = hasSocialAll || tags.Contains(SocialRedditTag);
            bool hasTwitter = hasSocialAll || tags.Contains(SocialTwitterTag);

            if (!hasReddit && !hasTwitter)
                return;

            if (tags.Contains(SocialDisableTag))
                return;

            if (hasReddit)
                await TryPostToReddit(store, post);

            if (hasTwitter)
                await TryPostToTwitter(store, post);
        }

        private static async Task TryPostToReddit(IDocumentStore store, Post post)
        {
            var title = post.Social?.RedditTitle;
            if (string.IsNullOrWhiteSpace(title))
                title = System.Net.WebUtility.HtmlDecode(post.Title);

            if (string.IsNullOrWhiteSpace(title))
            {
                _log.Info("No Reddit title available, skipping post {PostId}", post.Id);
                return;
            }

            using var session = store.OpenSession();
            var blogConfig = session.Load<BlogConfig>("Blog/Config");

            var subreddits = RedditHelper.ParseSubreddits(blogConfig);
            if (subreddits.Count == 0)
            {
                _log.Info("No subreddits configured, skipping Reddit post {PostId}", post.Id);
                return;
            }

            if (string.IsNullOrEmpty(blogConfig?.RedditUser) ||
                string.IsNullOrEmpty(blogConfig?.RedditPassword) ||
                string.IsNullOrEmpty(blogConfig?.RedditClientAppId) ||
                string.IsNullOrEmpty(blogConfig?.RedditClientSecret))
            {
                _log.Info("Reddit not fully configured, skipping post {PostId}", post.Id);
                return;
            }

            var postUrl = PostHelper.Url(post);

            RedditSharp.Reddit reddit;
            try
            {
                var agent = new RedditSharp.BotWebAgent(
                    blogConfig.RedditUser,
                    blogConfig.RedditPassword,
                    blogConfig.RedditClientAppId,
                    blogConfig.RedditClientSecret,
                    "http://localhost");
                reddit = new RedditSharp.Reddit(agent);
            }
            catch (Exception e)
            {
                _log.Error(e, "Reddit authentication failed for {PostId}", post.Id);
                EnqueueSocialFailureEmail(store, post, "Reddit", "", e.ToString());
                return;
            }

            foreach (var subredditName in subreddits)
            {
                try
                {
                    var subreddit = await reddit.GetSubredditAsync(subredditName);
                    await subreddit.SubmitPostAsync(title, postUrl, resubmit: false);
                    _log.Info("Submitted post {PostId} to {Subreddit}", post.Id, subredditName);
                }
                catch (RedditSharp.DuplicateLinkException)
                {
                    _log.Info("Post {PostId} already submitted to {Subreddit}", post.Id, subredditName);
                }
                catch (Exception e)
                {
                    _log.Error(e, "Failed to submit post {PostId} to {Subreddit}", post.Id, subredditName);
                    EnqueueSocialFailureEmail(store, post, "Reddit", subredditName, e.ToString());
                }
            }
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
