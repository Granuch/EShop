using EShop.BuildingBlocks.Application.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// DEBT-16. Stores each cache-key family's version in the distributed cache itself.
///
/// <para>
/// <b>Why a stored counter rather than deleting keys.</b> <see cref="IDistributedCache"/> has no
/// SCAN, so a family whose keys embed arbitrary query parameters cannot be enumerated and cannot
/// be deleted. Folding a version into the key turns eviction into a single write: bump it and
/// every previously-written key in that family is simply no longer addressed. The stale entries
/// are left to expire on their own TTL, which costs a little memory and saves an unbounded scan.
/// </para>
///
/// <para>
/// <b>Losing the version is safe, and that is deliberate.</b> If the entry is evicted or the cache
/// restarts cold, <see cref="GetVersionAsync"/> mints a fresh value rather than resurrecting the
/// old one — a new key space, so every read misses and repopulates. The failure mode is a burst of
/// cache misses, never a stale read. That is the right way round: the opposite design, seeding a
/// deterministic default like "1", would make a cold cache re-address keys written before the last
/// bump and serve data a write had already invalidated.
/// </para>
///
/// <para>
/// A bump is not atomic across instances (read, increment, write), but it does not need to be:
/// concurrent bumps race to write <i>different</i> new values, and any value other than the one
/// the old keys were written under invalidates them just as effectively.
/// </para>
/// </summary>
public sealed class DistributedCacheKeyVersionProvider : ICacheKeyVersionProvider
{
    private const string KeyPrefix = "cachever:";

    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheKeyVersionProvider> _logger;

    public DistributedCacheKeyVersionProvider(
        IDistributedCache cache,
        ILogger<DistributedCacheKeyVersionProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GetVersionAsync(string family, CancellationToken cancellationToken = default)
    {
        var stored = await _cache.GetStringAsync(KeyPrefix + family, cancellationToken);
        if (!string.IsNullOrEmpty(stored))
        {
            return stored;
        }

        var seeded = NewVersion();
        await WriteAsync(family, seeded, cancellationToken);
        return seeded;
    }

    public async Task BumpVersionAsync(string family, CancellationToken cancellationToken = default)
    {
        var bumped = NewVersion();
        await WriteAsync(family, bumped, cancellationToken);

        _logger.LogDebug(
            "Bumped cache key version for family {Family} to {Version}; all previously cached "
            + "entries in that family are now unreachable", family, bumped);
    }

    /// <summary>
    /// Time-ordered and random. Ordering makes a stored version readable when debugging a stale
    /// read; the random tail is what stops two bumps inside the same tick colliding and leaving
    /// the older keys addressable.
    /// </summary>
    private static string NewVersion()
        => $"{DateTime.UtcNow.Ticks:x}{Random.Shared.Next(0x1000, 0xffff):x}";

    /// <summary>
    /// No expiration: the version must outlive the entries it addresses. Giving it a TTL shorter
    /// than the data's would silently rotate the key space and turn every read into a miss.
    /// </summary>
    private Task WriteAsync(string family, string version, CancellationToken cancellationToken)
        => _cache.SetStringAsync(
            KeyPrefix + family,
            version,
            new DistributedCacheEntryOptions(),
            cancellationToken);
}
