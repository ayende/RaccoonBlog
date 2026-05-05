using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using RaccoonBlog.Web.Models;
using Raven.Client.Documents;

namespace RaccoonBlog.Web.Services;

public class BannerService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IDocumentStore _documentStore;
    private readonly IMemoryCache _cache;
    private readonly CacheSignalService _cacheSignal;

    public BannerService(IDocumentStore documentStore, IMemoryCache cache, CacheSignalService cacheSignal)
    {
        _documentStore = documentStore;
        _cache = cache;
        _cacheSignal = cacheSignal;
    }

    public List<BannerItem> GetActiveBanners()
    {
        var cacheKey = $"blog-banners-{_cacheSignal.GetToken(CacheKeys.BannerArea)}";

        if (_cache.TryGetValue(cacheKey, out List<BannerItem> cached))
            return cached;

        using var session = _documentStore.OpenSession();
        var doc = session.Load<BlogBanners>(BlogBanners.DocumentId);

        var now = DateTimeOffset.UtcNow;
        var banners = doc?.Banners
            .Where(b => b.Enabled
                && (b.StartDate == null || b.StartDate <= now)
                && (b.EndDate == null || b.EndDate >= now))
            .ToList() ?? new List<BannerItem>();

        _cache.Set(cacheKey, banners, CacheDuration);
        return banners;
    }
}
