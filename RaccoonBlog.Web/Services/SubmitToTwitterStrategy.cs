using System;
using System.Net.Http;
using System.Text;
using NLog;
using Newtonsoft.Json.Linq;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents.Session;

namespace RaccoonBlog.Web.Services
{
    public class SubmitToTwitterStrategy
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string TwitterApiUrl = "https://api.x.com/2/tweets";
        private const int MaxTweetLength = 280;

        public void Publish(SocialPublishCommand cmd, IDocumentSession session)
        {
            var post = session.Load<Post>(cmd.PostId);
            if (post == null)
            {
                _log.Warn("Post {0} not found for Twitter command {1}", cmd.PostId, cmd.Id);
                return;
            }

            var config = session.Load<BlogConfig>(BlogConfig.Key);
            var (apiKey, apiSecret, accessToken, accessTokenSecret) = GetCredentials(config, cmd.Account);

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(accessToken))
            {
                _log.Warn("Twitter credentials not configured for account '{0}'", cmd.Account ?? "default");
                return;
            }

            var tweetText = BuildTweetText(post);

            var payload = new JObject { ["text"] = tweetText }.ToString();
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var authHeader = TwitterOAuthHelper.GenerateAuthorizationHeader(
                "POST", TwitterApiUrl, apiKey, apiSecret, accessToken, accessTokenSecret);

            using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", authHeader);

                var response = httpClient.PostAsync(TwitterApiUrl, content).GetAwaiter().GetResult();
                var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(responseBody);
                    var tweetId = json["data"]?["id"]?.Value<string>();
                    cmd.TweetId = tweetId;
                    _log.Info("Tweet posted successfully for post {0}: id={1}", cmd.PostId, tweetId);
                }
                else
                {
                    _log.Error("Twitter API error for post {0}: {1} {2}", cmd.PostId, response.StatusCode, responseBody);
                    throw new InvalidOperationException($"Twitter API returned {response.StatusCode}: {responseBody}");
                }
            }
        }

        private static string BuildTweetText(Post post)
        {
            var url = PostHelper.Url(post);
            var title = post.Title;
            var description = post.SeoMetaDescription ?? "";

            // Format: "Title — Description URL"
            var candidate = $"{title} — {description} {url}";

            if (candidate.Length <= MaxTweetLength)
                return candidate;

            // Try without description: "Title URL"
            candidate = $"{title} {url}";
            if (candidate.Length <= MaxTweetLength)
                return candidate;

            // Truncate title to fit: "... URL"
            var maxTitleLen = MaxTweetLength - 5 - url.Length; // 5 for " ... "
            if (maxTitleLen > 10)
            {
                var truncated = title.Length <= maxTitleLen ? title : title.Substring(0, maxTitleLen).TrimEnd() + "…";
                return $"{truncated} {url}";
            }

            // Last resort: just the URL
            return url;
        }

        private static (string apiKey, string apiSecret, string accessToken, string accessTokenSecret) GetCredentials(
            BlogConfig config, string account)
        {
            if (string.Equals(account, "ayende", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    config.TwitterAccountAyendeApiKey,
                    config.TwitterAccountAyendeApiSecret,
                    config.TwitterAccountAyendeAccessToken,
                    config.TwitterAccountAyendeAccessTokenSecret
                );
            }

            // Default to RavenDB account
            return (
                config.TwitterAccountRavendbApiKey,
                config.TwitterAccountRavendbApiSecret,
                config.TwitterAccountRavendbAccessToken,
                config.TwitterAccountRavendbAccessTokenSecret
            );
        }
    }
}
