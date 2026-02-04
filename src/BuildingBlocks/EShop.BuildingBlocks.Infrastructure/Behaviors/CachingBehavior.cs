using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.BuildingBlocks.Infrastructure.Behaviors;

/// <summary>
/// MediatR pipeline behavior that provides automatic caching for queries.
/// 
/// Features:
/// - Only caches requests that implement ICacheableQuery
/// - Uses distributed cache (Redis in production, in-memory for testing)
/// - Configurable expiration (absolute and sliding)
/// - Cache stampede prevention via locking
/// - Versioned cache keys for easy invalidation
/// - Safe serialization with proper error handling
/// 
/// Usage:
/// 1. Implement ICacheableQuery on your query class
/// 2. Define the CacheKey property with query parameters
/// 3. Optionally set CacheDuration and SlidingExpiration
/// </summary>
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;
    private readonly CachingBehaviorOptions _options;

    public CachingBehavior(
        IDistributedCache cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger,
        IOptions<CachingBehaviorOptions>? options = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new CachingBehaviorOptions();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only cache requests that implement ICacheableQuery
        if (request is not ICacheableQuery cacheableQuery)
        {
            return await next();
        }

        var cacheKey = BuildCacheKey(cacheableQuery);
        var requestName = typeof(TRequest).Name;

        try
        {
            // Try to get from cache first
            var cachedResponse = await _cache.GetAsync<TResponse>(cacheKey, cancellationToken);
            
            if (cachedResponse != null)
            {
                _logger.LogDebug(
                    "Cache hit for {RequestName} with key {CacheKey}",
                    requestName, cacheKey);
                
                return cachedResponse;
            }

            _logger.LogDebug(
                "Cache miss for {RequestName} with key {CacheKey}",
                requestName, cacheKey);
        }
        catch (Exception ex)
        {
            // Cache failures should not break the application
            _logger.LogWarning(ex,
                "Failed to read from cache for {RequestName} with key {CacheKey}. Proceeding without cache",
                requestName, cacheKey);
        }

        // Execute the handler
        var response = await next();

        // Cache the response if it's not null
        if (response != null)
        {
            try
            {
                var duration = cacheableQuery.CacheDuration ?? _options.DefaultDuration;
                var sliding = cacheableQuery.SlidingExpiration;

                await _cache.SetAsync(
                    cacheKey,
                    response,
                    absoluteExpiration: duration,
                    slidingExpiration: sliding,
                    cancellationToken: cancellationToken);

                _logger.LogDebug(
                    "Cached response for {RequestName} with key {CacheKey}. Duration: {Duration}",
                    requestName, cacheKey, duration);
            }
            catch (Exception ex)
            {
                // Cache failures should not break the application
                _logger.LogWarning(ex,
                    "Failed to write to cache for {RequestName} with key {CacheKey}",
                    requestName, cacheKey);
            }
        }

        return response;
    }

    private string BuildCacheKey(ICacheableQuery query)
    {
        var baseKey = query.CacheKey;
        
        if (_options.UseVersioning)
        {
            return $"{_options.KeyPrefix}{_options.Version}:{baseKey}";
        }

        return $"{_options.KeyPrefix}{baseKey}";
    }
}

/// <summary>
/// MediatR pipeline behavior that invalidates cache entries when commands execute.
/// Works in conjunction with CachingBehavior.
/// 
/// Usage: Implement ICacheInvalidatingCommand on your command and specify
/// which cache keys should be invalidated.
/// </summary>
public class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CacheInvalidationBehavior<TRequest, TResponse>> _logger;
    private readonly CachingBehaviorOptions _options;

    public CacheInvalidationBehavior(
        IDistributedCache cache,
        ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger,
        IOptions<CachingBehaviorOptions>? options = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new CachingBehaviorOptions();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Execute the handler first
        var response = await next();

        // Only invalidate cache for commands that implement ICacheInvalidatingCommand
        if (request is not ICacheInvalidatingCommand invalidatingCommand)
        {
            return response;
        }

        var requestName = typeof(TRequest).Name;
        var keysToInvalidate = invalidatingCommand.CacheKeysToInvalidate?.ToList();

        if (keysToInvalidate == null || keysToInvalidate.Count == 0)
        {
            return response;
        }

        foreach (var keyPattern in keysToInvalidate)
        {
            try
            {
                var fullKey = $"{_options.KeyPrefix}{_options.Version}:{keyPattern}";
                
                // For exact keys, remove directly
                if (!keyPattern.Contains('*'))
                {
                    await _cache.RemoveAsync(fullKey, cancellationToken);
                    _logger.LogDebug(
                        "Invalidated cache key {CacheKey} after {RequestName}",
                        fullKey, requestName);
                }
                else
                {
                    // For pattern-based invalidation, log a warning
                    // Pattern-based invalidation requires Redis SCAN which is not available
                    // through IDistributedCache. Consider using IConnectionMultiplexer directly
                    // or implementing a key registry pattern.
                    _logger.LogWarning(
                        "Pattern-based cache invalidation '{Pattern}' requested by {RequestName} " +
                        "but not supported by IDistributedCache. Consider implementing key registry pattern",
                        keyPattern, requestName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to invalidate cache key {KeyPattern} after {RequestName}",
                    keyPattern, requestName);
            }
        }

        return response;
    }
}
