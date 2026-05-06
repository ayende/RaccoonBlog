using System;
using System.Collections.Generic;
using System.Linq;
using RaccoonBlog.Web.Models;

namespace RaccoonBlog.Web.Services
{
    public static class SocialTagParser
    {
        private const string SocialPrefix = "@social";
        private const string DisableTag = "@social/disable";

        private static readonly HashSet<string> ValidTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "twitter", "reddit", "discord", "github"
        };

        public static bool HasSocialTags(ICollection<string> tags)
        {
            if (tags == null || tags.Count == 0)
                return false;
            return tags.Any(t => t.StartsWith(SocialPrefix, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsDisabled(ICollection<string> tags)
        {
            if (tags == null)
                return false;
            return tags.Any(t => t.Equals(DisableTag, StringComparison.OrdinalIgnoreCase));
        }

        public static List<(string Target, string Account)> Parse(ICollection<string> tags, BlogConfig config)
        {
            var result = new List<(string Target, string Account)>();

            if (tags == null || tags.Count == 0)
                return result;

            if (IsDisabled(tags))
                return result;

            var socialTags = tags
                .Where(t => t.StartsWith(SocialPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var tag in socialTags)
            {
                var parts = tag.Split('/');

                // @social (bare prefix → all configured targets)
                if (parts.Length == 1 && parts[0].Equals(SocialPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddRange(GetAllConfiguredTargets(config));
                    continue;
                }

                // @social/disable — already handled
                if (parts.Length >= 2 && parts[1].Equals("disable", StringComparison.OrdinalIgnoreCase))
                    continue;

                // @social/target or @social/target/account
                if (parts.Length >= 2 && ValidTargets.Contains(parts[1]))
                {
                    var target = parts[1].ToLowerInvariant();
                    var account = parts.Length >= 3 ? parts[2].ToLowerInvariant() : null;
                    result.Add((target, account));
                }
            }

            // Deduplicate — same target+account shouldn't generate multiple commands
            return result
                .GroupBy(x => (x.Target, x.Account))
                .Select(g => g.First())
                .ToList();
        }

        private static List<(string Target, string Account)> GetAllConfiguredTargets(BlogConfig config)
        {
            var targets = new List<(string Target, string Account)>();

            if (string.IsNullOrEmpty(config.TwitterAccountRavendbApiKey) == false)
                targets.Add(("twitter", "ravendb"));
            if (string.IsNullOrEmpty(config.TwitterAccountAyendeApiKey) == false)
                targets.Add(("twitter", "ayende"));
            if (string.IsNullOrEmpty(config.RedditSubredditsToSubmitToOnPublish) == false)
                targets.Add(("reddit", null));
            if (string.IsNullOrEmpty(config.DiscordWebhookUrl) == false)
                targets.Add(("discord", null));
            if (string.IsNullOrEmpty(config.GitHubPersonalAccessToken) == false)
                targets.Add(("github", null));

            return targets;
        }
    }
}
