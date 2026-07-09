using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;
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

        // Reduces an API error response to a compact, non-sensitive summary for the
        // failure email: the documented error fields when the body is JSON, otherwise
        // a length-capped copy. Avoids dumping an arbitrary response body out over SMTP.
        public static string SummarizeErrorBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "(empty response body)";

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var name in new[] { "error", "error_description", "title", "detail" })
                    {
                        if (root.TryGetProperty(name, out var value))
                            parts.Add($"{name}: {value}");
                    }
                    if (parts.Count > 0)
                        return string.Join("\n", parts);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON — fall through to the capped raw body.
            }

            const int cap = 500;
            return body.Length <= cap ? body : body.Substring(0, cap) + "… (truncated)";
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

                if (string.IsNullOrEmpty(blogConfig?.TwitterApiKey) ||
                    string.IsNullOrEmpty(blogConfig?.TwitterApiSecret) ||
                    string.IsNullOrEmpty(blogConfig?.TwitterAccessToken) ||
                    string.IsNullOrEmpty(blogConfig?.TwitterAccessTokenSecret))
                {
                    _log.Info("Twitter OAuth not configured, skipping post {PostId}", post.Id);
                    return;
                }

                var postUrl = PostHelper.Url(post);
                var tweetText = post.Social.TwitterText + " " + postUrl;

                const string endpoint = "https://api.x.com/2/tweets";
                var nonce = Guid.NewGuid().ToString("N");
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var authHeader = BuildOAuth1Header("POST", endpoint,
                    blogConfig.TwitterApiKey, blogConfig.TwitterApiSecret,
                    blogConfig.TwitterAccessToken, blogConfig.TwitterAccessTokenSecret,
                    nonce, timestamp);

                using var client = new System.Net.Http.HttpClient();
                // OAuth headers contain characters .NET's strict Authorization parser rejects,
                // so add the header without validation.
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);

                var content = new System.Net.Http.StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { text = tweetText }),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await client.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    _log.Info("Tweet posted for {PostId}", post.Id);
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var detail = $"Twitter API returned {(int)response.StatusCode} {response.StatusCode}.\nResponse detail:\n{SummarizeErrorBody(body)}";
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

        // Builds an OAuth 1.0a "Authorization" header value (HMAC-SHA1) for a request whose
        // body is JSON (as with POST /2/tweets). A JSON payload is not part of the OAuth 1.0a
        // signature base string, so only the oauth_* protocol parameters are signed. (If this
        // ever needs to sign a form-encoded body or query parameters, add them to the dict
        // passed to ComputeOAuth1Signature — which already handles arbitrary request params.)
        public static string BuildOAuth1Header(
            string method, string url,
            string apiKey, string apiSecret, string accessToken, string accessTokenSecret,
            string nonce, string timestamp)
        {
            var oauthParams = new System.Collections.Generic.Dictionary<string, string>
            {
                ["oauth_consumer_key"] = apiKey,
                ["oauth_nonce"] = nonce,
                ["oauth_signature_method"] = "HMAC-SHA1",
                ["oauth_timestamp"] = timestamp,
                ["oauth_token"] = accessToken,
                ["oauth_version"] = "1.0",
            };

            var signature = ComputeOAuth1Signature(method, url, oauthParams, apiSecret, accessTokenSecret);

            var headerParams = new System.Collections.Generic.Dictionary<string, string>(oauthParams)
            {
                ["oauth_signature"] = signature,
            };

            return "OAuth " + string.Join(", ", headerParams
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{PercentEncode(kv.Key)}=\"{PercentEncode(kv.Value)}\""));
        }

        // Computes the OAuth 1.0a HMAC-SHA1 signature (RFC 5849 §3.4) over the request:
        // signature = base64(HMAC-SHA1(baseString, "apiSecret&accessTokenSecret")).
        public static string ComputeOAuth1Signature(
            string method, string url,
            System.Collections.Generic.IDictionary<string, string> signatureParams,
            string apiSecret, string accessTokenSecret)
        {
            var paramString = string.Join("&", signatureParams
                .Select(kv => new { K = PercentEncode(kv.Key), V = PercentEncode(kv.Value) })
                .OrderBy(x => x.K, StringComparer.Ordinal)
                .ThenBy(x => x.V, StringComparer.Ordinal)
                .Select(x => $"{x.K}={x.V}"));

            var baseString = $"{method.ToUpperInvariant()}&{PercentEncode(url)}&{PercentEncode(paramString)}";
            var signingKey = $"{PercentEncode(apiSecret)}&{PercentEncode(accessTokenSecret)}";

            using var hmac = new System.Security.Cryptography.HMACSHA1(System.Text.Encoding.ASCII.GetBytes(signingKey));
            var hash = hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes(baseString));
            return Convert.ToBase64String(hash);
        }

        // RFC 3986 percent-encoding as required by OAuth 1.0a: unreserved characters pass
        // through, everything else becomes %XX with uppercase hex.
        // NOTE: this deliberately iterates the UTF-8 *bytes* of the value (multi-byte code
        // points become several %XX escapes). Do not "simplify" it to iterate string chars —
        // that would corrupt any non-ASCII input that ever reaches the signature.
        private static string PercentEncode(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                char c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_' || c == '~')
                    sb.Append(c);
                else
                    sb.Append('%').Append(((int)b).ToString("X2"));
            }
            return sb.ToString();
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
