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
/// DEBT-16. Marks a cacheable query whose keys <b>cannot</b> be invalidated by exact key.
///
/// <para>
/// <see cref="ICacheInvalidatingCommand"/> only removes keys it can name, and a list query's key
/// embeds every filter, sort and page parameter — so the set of live keys is unbounded and a write
/// cannot enumerate them. Catalog's <c>products:list:*</c> family was the case that forced this:
/// after any product write, list results stayed stale for the full 5-minute TTL, a fact that had
/// been copy-pasted as a comment into four command handlers rather than fixed.
/// </para>
///
/// <para>
/// The fix is indirection. <see cref="CachingBehavior"/> folds the family's current version into
/// every key it writes, so bumping that one counter makes the entire family unreachable in a
/// single operation, whatever the parameters were. Old entries are not deleted — they simply
/// stop being addressed and expire on their own TTL, which is the point: no SCAN, no key
/// enumeration, O(1).
/// </para>
/// </summary>
public interface IVersionedCacheKey
{
    /// <summary>
    /// The family this query's results belong to, e.g. <c>products:list</c>. Every query sharing a
    /// family is invalidated together, so keep it as narrow as the writes that must evict it.
    /// </summary>
    string CacheKeyFamily { get; }
}

/// <summary>
/// Reads and bumps the per-family cache version behind <see cref="IVersionedCacheKey"/>.
///
/// <para>
/// The version is itself stored in the distributed cache. A lost version (eviction, restart,
/// cold Redis) is safe by construction: it restarts from a fresh value, which addresses a new
/// key space and therefore reads as a miss rather than as stale data.
/// </para>
/// </summary>
public interface ICacheKeyVersionProvider
{
    Task<string> GetVersionAsync(string family, CancellationToken cancellationToken = default);

    /// <summary>Makes every key currently in <paramref name="family"/> unreachable.</summary>
    Task BumpVersionAsync(string family, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Versioned key families to bump when this command executes — see
    /// <see cref="IVersionedCacheKey"/>. Use this for any result set whose keys embed parameters
    /// and therefore cannot be named, which is the case <see cref="CacheKeysToInvalidate"/>
    /// structurally cannot express.
    ///
    /// <para>
    /// Its absence is why two invalidation styles used to coexist: with no way to say "bump a
    /// family", Catalog's handlers bypassed <c>CacheInvalidationBehavior</c> entirely and called a
    /// service-local <c>ICacheInvalidator</c>, while Ordering's used the shared context. Defaulted
    /// to empty so the many commands that only name exact keys need no change.
    /// </para>
    ///
    /// <para>
    /// Bumping requires an <see cref="ICacheKeyVersionProvider"/> registration; without one the
    /// behavior logs a warning and does nothing, the same failure posture as a wildcard key.
    /// </para>
    /// </summary>
    IEnumerable<string> CacheFamiliesToInvalidate => [];
}

/// <summary>
/// Scoped context for dynamic cache invalidation metadata produced during command handling.
/// Use when invalidation keys are known only after loading domain data — a product's
/// <c>CategoryId</c>, say, which the command does not carry.
/// </summary>
public interface ICacheInvalidationContext
{
    void AddKey(string key);
    void AddKeys(IEnumerable<string> keys);
    IReadOnlyCollection<string> GetKeys();

    /// <summary>Queues a versioned key family for bumping. See
    /// <see cref="ICacheInvalidatingCommand.CacheFamiliesToInvalidate"/>.</summary>
    void AddFamily(string family);
    void AddFamilies(IEnumerable<string> families);
    IReadOnlyCollection<string> GetFamilies();

    /// <summary>Clears both keys and families.</summary>
    void Clear();
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
