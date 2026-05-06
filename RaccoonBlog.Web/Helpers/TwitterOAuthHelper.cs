using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RaccoonBlog.Web.Helpers
{
    public static class TwitterOAuthHelper
    {
        public static string GenerateAuthorizationHeader(
            string method,
            string url,
            string consumerKey,
            string consumerSecret,
            string accessToken,
            string accessTokenSecret,
            Dictionary<string, string> additionalParams = null)
        {
            var oauthParams = new Dictionary<string, string>
            {
                {"oauth_consumer_key", consumerKey},
                {"oauth_nonce", Guid.NewGuid().ToString("N")},
                {"oauth_signature_method", "HMAC-SHA1"},
                {"oauth_timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()},
                {"oauth_token", accessToken},
                {"oauth_version", "1.0"}
            };

            var allParams = new Dictionary<string, string>(oauthParams);
            if (additionalParams != null)
            {
                foreach (var kvp in additionalParams)
                    allParams[kvp.Key] = kvp.Value;
            }

            var signatureBaseString = BuildSignatureBaseString(method, url, allParams);
            var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(accessTokenSecret)}";
            var signature = ComputeHmacSha1(signingKey, signatureBaseString);

            oauthParams["oauth_signature"] = signature;

            return "OAuth " + string.Join(", ",
                oauthParams.OrderBy(kvp => kvp.Key)
                    .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}=\"{Uri.EscapeDataString(kvp.Value)}\""));
        }

        private static string BuildSignatureBaseString(string method, string url, Dictionary<string, string> parameters)
        {
            var parameterString = string.Join("&",
                parameters.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                    .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            return $"{method.ToUpper()}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(parameterString)}";
        }

        private static string ComputeHmacSha1(string key, string baseString)
        {
            using (var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(key)))
            {
                var hash = hmac.ComputeHash(Encoding.ASCII.GetBytes(baseString));
                return Convert.ToBase64String(hash);
            }
        }
    }
}
