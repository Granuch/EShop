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

    public CacheInvalidator(
        IDistributedCache cache,
        ILogger<CacheInvalidator> logger,
        IOptions<CachingBehaviorOptions>? options = null)
    {
        _cache = cache;
        _logger = logger;
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
}
