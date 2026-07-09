using System.Collections.Generic;
using RaccoonBlog.Web.Infrastructure;
using Xunit;

namespace RaccoonBlog.IntegrationTests.Infrastructure
{
    public class TwitterPostingTests
    {
        // Uses the inputs from Twitter's canonical OAuth 1.0a documentation example
        // (chosen because they exercise percent-encoding of space, '+', ',' and '!', plus
        // the double-encoding of the parameter string in the base string). The expected
        // signature below was cross-verified against an independent HMAC-SHA1 reference
        // implementation (Python's hmac) for these exact inputs. Pinning to it guards the
        // whole signing pipeline — base-string assembly, RFC 3986 encoding, ordinal sort,
        // HMAC-SHA1, base64 — against silent regressions (a signature bug is a runtime 401).
        [Fact]
        public void ComputeOAuth1Signature_matches_known_reference()
        {
            var parameters = new Dictionary<string, string>
            {
                ["status"] = "Hello Ladies + Gentlemen, a signed OAuth request!",
                ["include_entities"] = "true",
                ["oauth_consumer_key"] = "xvz1evFS4wEEPTGEFPHBog",
                ["oauth_nonce"] = "kYjzVBB8Y0ZFabxSWbWovY3uYSQ2pTgmZeNu2VS4cg",
                ["oauth_signature_method"] = "HMAC-SHA1",
                ["oauth_timestamp"] = "1318622958",
                ["oauth_token"] = "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb",
                ["oauth_version"] = "1.0",
            };

            var signature = SocialPostingSubscription.ComputeOAuth1Signature(
                "POST",
                "https://api.twitter.com/1.1/statuses/update.json",
                parameters,
                "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Y7fAq9jPxbQ",
                "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE");

            Assert.Equal("vCy+TETdw3tYVtjypxY4qpaR7Dw=", signature);
        }

        [Fact]
        public void BuildOAuth1Header_emits_oauth_scheme_with_signature_and_fields()
        {
            var header = SocialPostingSubscription.BuildOAuth1Header(
                "POST", "https://api.x.com/2/tweets",
                "api-key", "api-secret", "access-token", "access-secret",
                "nonce123", "1700000000");

            Assert.StartsWith("OAuth ", header);
            Assert.Contains("oauth_consumer_key=\"api-key\"", header);
            Assert.Contains("oauth_token=\"access-token\"", header);
            Assert.Contains("oauth_signature_method=\"HMAC-SHA1\"", header);
            Assert.Contains("oauth_signature=", header);
            // The JSON payload is never part of the signature, so the header does not depend on it.
            Assert.DoesNotContain("text", header);
        }

        [Fact]
        public void SummarizeErrorBody_extracts_error_fields_from_json()
        {
            var body = "{\"error\":\"invalid_request\",\"error_description\":\"bad token\"}";

            var result = SocialPostingSubscription.SummarizeErrorBody(body);

            Assert.Contains("error: invalid_request", result);
            Assert.Contains("error_description: bad token", result);
            Assert.DoesNotContain("{", result);
        }

        [Fact]
        public void SummarizeErrorBody_caps_long_non_json_body()
        {
            var body = new string('x', 1000);

            var result = SocialPostingSubscription.SummarizeErrorBody(body);

            Assert.True(result.Length < 600);
            Assert.EndsWith("(truncated)", result);
        }

        [Fact]
        public void SummarizeErrorBody_handles_empty()
        {
            Assert.Equal("(empty response body)", SocialPostingSubscription.SummarizeErrorBody(""));
        }
    }
}
