using System;
using System.Collections.Concurrent;

namespace RaccoonBlog.Web.Services
{
    public static class CacheKeys
    {
        public const string SectionArea = "Section_Area";
        public const string BannerArea = "Banner_Area";
    }

    public class CacheSignalService
    {
        private readonly ConcurrentDictionary<string, string> _tokens = new();

        public string GetToken(string key)
        {
            return _tokens.GetOrAdd(key, _ => Guid.NewGuid().ToString());
        }

        public void Invalidate(string key)
        {
            _tokens[key] = Guid.NewGuid().ToString();
        }
    }
}
