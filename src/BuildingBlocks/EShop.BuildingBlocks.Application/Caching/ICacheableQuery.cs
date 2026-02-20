namespace EShop.BuildingBlocks.Application.Caching;

/// <summary>
/// Marker interface for queries that should be cached.
/// Implement this interface on your query classes to enable automatic caching.
/// </summary>
public interface ICacheableQuery
{
    /// <summary>
    /// The unique cache key for this query.
    /// Should include all parameters that affect the result.
    /// Example: "products:category:123:page:1:size:20"
    /// </summary>
    string CacheKey { get; }

    /// <summary>
    /// How long the result should be cached (absolute expiration).
    /// If null, uses the default cache duration.
    /// </summary>
    TimeSpan? CacheDuration { get; }

    /// <summary>
    /// Sliding expiration time. Resets each time the cache is accessed.
    /// If null, no sliding expiration is used.
    /// </summary>
    TimeSpan? SlidingExpiration { get; }
}

/// <summary>
/// Base implementation of ICacheableQuery with sensible defaults.
/// Inherit from this to simplify cache configuration.
/// </summary>
public abstract class CacheableQuery : ICacheableQuery
{
    /// <summary>
    /// Override to provide the cache key based on query parameters.
    /// </summary>
    public abstract string CacheKey { get; }

    /// <summary>
    /// Default: 5 minutes. Override for different durations.
    /// </summary>
    public virtual TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Default: null (no sliding expiration). Override if needed.
    /// </summary>
    public virtual TimeSpan? SlidingExpiration => null;
}

/// <summary>
/// Interface for commands that can invalidate cache entries.
/// Use this on commands that modify data that is cached.
/// </summary>
public interface ICacheInvalidatingCommand
{
    /// <summary>
    /// Exact cache keys to invalidate when this command executes.
    /// Each key must be an exact match — pattern/wildcard-based invalidation
    /// is not supported by IDistributedCache.
    /// </summary>
    IEnumerable<string> CacheKeysToInvalidate { get; }
}

/// <summary>
/// Options for cache behavior configuration.
/// </summary>
public class CachingBehaviorOptions
{
    /// <summary>
    /// Default cache duration when ICacheableQuery.CacheDuration is null.
    /// </summary>
    public TimeSpan DefaultDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Prefix for all cache keys to avoid collisions with other applications.
    /// </summary>
    public string KeyPrefix { get; set; } = "eshop:";

    /// <summary>
    /// Whether to include a version in the cache key for cache invalidation.
    /// </summary>
    public bool UseVersioning { get; set; } = true;

    /// <summary>
    /// Current cache version. Increment to invalidate all cached data.
    /// </summary>
    public string Version { get; set; } = "v1";
}
