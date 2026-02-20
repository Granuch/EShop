namespace EShop.Catalog.Application.Abstractions;

/// <summary>
/// Abstraction for cache invalidation operations.
/// Implemented in Infrastructure to avoid direct IDistributedCache dependency in Application.
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Removes a specific cache entry by key.
    /// </summary>
    Task InvalidateAsync(string cacheKey, CancellationToken cancellationToken = default);
}
