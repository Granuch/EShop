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

    /// <summary>
    /// DEBT-16. Invalidates an entire versioned key family in one operation — used for
    /// <c>products:list</c>, whose keys embed every filter/sort/page parameter and therefore
    /// cannot be enumerated or named individually.
    ///
    /// <para>
    /// This does not delete anything: it bumps the family's version so the existing keys stop
    /// being addressed and lapse on their own TTL. Cheap and O(1), but it means a bump is not
    /// observable by looking for absent keys — check the version entry instead.
    /// </para>
    /// </summary>
    Task InvalidateFamilyAsync(string family, CancellationToken cancellationToken = default);
}
