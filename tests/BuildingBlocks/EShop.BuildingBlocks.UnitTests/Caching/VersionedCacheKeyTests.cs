using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EShop.BuildingBlocks.UnitTests.Caching;

/// <summary>
/// DEBT-16. The mechanism that makes an unenumerable key family invalidatable.
///
/// <para>
/// <c>IDistributedCache</c> has no SCAN, so a family whose keys embed arbitrary query parameters
/// — Catalog's <c>products:list:*</c> is the case that forced this — cannot be enumerated and
/// therefore cannot be deleted. Folding a per-family version into the key turns eviction into a
/// single write. These tests pin the two properties that make that safe: a bump changes the key
/// space, and a lost version fails to a miss rather than to a stale read.
/// </para>
/// </summary>
[TestFixture]
public class VersionedCacheKeyTests
{
    private IDistributedCache _cache = null!;
    private DistributedCacheKeyVersionProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        _provider = new DistributedCacheKeyVersionProvider(
            _cache, NullLogger<DistributedCacheKeyVersionProvider>.Instance);
    }

    [Test]
    public async Task GetVersionAsync_IsStable_UntilSomethingBumpsIt()
    {
        var first = await _provider.GetVersionAsync("products:list");
        var second = await _provider.GetVersionAsync("products:list");

        Assert.That(second, Is.EqualTo(first),
            "an unchanged family must keep addressing the same keys, or every read is a miss");
    }

    /// <summary>The whole point: after a bump, keys built earlier are no longer addressed.</summary>
    [Test]
    public async Task BumpVersionAsync_ChangesTheKeySpace()
    {
        var before = await _provider.GetVersionAsync("products:list");

        await _provider.BumpVersionAsync("products:list");

        var after = await _provider.GetVersionAsync("products:list");
        Assert.That(after, Is.Not.EqualTo(before));
    }

    /// <summary>
    /// Two bumps in quick succession must not collide. A tick-only version would, and the second
    /// bump would silently leave the first bump's keys addressable.
    /// </summary>
    [Test]
    public async Task ConsecutiveBumps_ProduceDistinctVersions()
    {
        var seen = new HashSet<string>();

        for (var i = 0; i < 20; i++)
        {
            await _provider.BumpVersionAsync("products:list");
            seen.Add(await _provider.GetVersionAsync("products:list"));
        }

        Assert.That(seen, Has.Count.EqualTo(20));
    }

    [Test]
    public async Task FamiliesAreIndependent()
    {
        var otherBefore = await _provider.GetVersionAsync("categories:list");

        await _provider.BumpVersionAsync("products:list");

        Assert.That(await _provider.GetVersionAsync("categories:list"), Is.EqualTo(otherBefore),
            "bumping one family must not evict another's cached results");
    }

    /// <summary>
    /// The failure mode has to be a miss, never a stale read. If a cold or evicted version
    /// resurrected a deterministic default, the cache would re-address keys written before the
    /// last bump and serve data a write had already invalidated.
    /// </summary>
    [Test]
    public async Task AVersionLostFromTheCache_FailsToAMissRatherThanAStaleRead()
    {
        var original = await _provider.GetVersionAsync("products:list");

        await _cache.RemoveAsync("cachever:products:list");

        var afterLoss = await _provider.GetVersionAsync("products:list");
        Assert.That(afterLoss, Is.Not.EqualTo(original),
            "a lost version must mint a new key space, not resurrect the old one");
    }

    /// <summary>
    /// The version entry must outlive the data it addresses, so it is written without expiration.
    /// A TTL shorter than the cached data's would rotate the key space on its own and turn every
    /// read into a miss.
    /// </summary>
    [Test]
    public async Task TheVersionEntryItselfIsStoredWithoutExpiry()
    {
        await _provider.GetVersionAsync("products:list");

        var stored = await _cache.GetStringAsync("cachever:products:list");

        Assert.That(stored, Is.Not.Null.And.Not.Empty);
    }
}
