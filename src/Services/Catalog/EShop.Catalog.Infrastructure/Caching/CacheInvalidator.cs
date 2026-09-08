using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Catalog.Infrastructure.Caching;

/// <summary>
/// Infrastructure implementation of ICacheInvalidator using IDistributedCache.
/// Builds full cache keys using CachingBehaviorOptions prefix and version.
/// </summary>
public class CacheInvalidator : ICacheInvalidator
{
    private readonly IDistributedCache _cache;
    private readonly CachingBehaviorOptions _options;
    private readonly ILogger<CacheInvalidator> _logger;
    private readonly ICacheKeyVersionProvider _versionProvider;

    public CacheInvalidator(
        IDistributedCache cache,
        ILogger<CacheInvalidator> logger,
        ICacheKeyVersionProvider versionProvider,
        IOptions<CachingBehaviorOptions>? options = null)
    {
        _cache = cache;
        _logger = logger;
        _versionProvider = versionProvider;
        _options = options?.Value ?? new CachingBehaviorOptions();
    }

    public async Task InvalidateAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullKey = $"{_options.KeyPrefix}{_options.Version}:{cacheKey}";
            await _cache.RemoveAsync(fullKey, cancellationToken);
            _logger.LogDebug("Invalidated cache key {CacheKey}", fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache key {CacheKey}", cacheKey);
        }
    }

    public async Task InvalidateFamilyAsync(string family, CancellationToken cancellationToken = default)
    {
        try
        {
            await _versionProvider.BumpVersionAsync(family, cancellationToken);
            _logger.LogDebug("Invalidated cache family {Family}", family);
        }
        catch (Exception ex)
        {
            // Same posture as InvalidateAsync above: a cache failure must not fail the write that
            // already committed. The cost of swallowing is a stale list for up to the TTL, which is
            // the behaviour this whole mechanism improves on rather than one it can make worse.
            _logger.LogWarning(ex, "Failed to invalidate cache family {Family}", family);
        }
    }
}
