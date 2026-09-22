using System.Globalization;
using System.Text;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Configuration;
using EShop.Basket.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Admin;

/// <summary>
/// Walks every stored basket with <c>SCAN … MATCH basket:user:* COUNT n</c> (Admin panel S14, #78/#79).
///
/// <para><b>Never <c>KEYS</c>.</b> <c>KEYS</c> walks the whole keyspace in one blocking call — every checkout, price sync
/// and outbox poll waits behind it. <c>SCAN</c> does about <see cref="ScanCount"/> keys of work per call, and a request
/// makes at most <see cref="MaxScanCallsPerPage"/> calls, so one admin page costs a bounded amount of server time
/// however large the keyspace grows. <c>Admin/BasketAdminScanTests</c> reads Redis's own command statistics to
/// pin both.</para>
///
/// <para><b>A page never exceeds its size, and never drops a key to stay under it.</b> <c>COUNT</c> is only a hint: one
/// call can return more matching keys than the page has room for. The page then stops part-way through that batch, and
/// the next request re-issues the same cursor and continues from the offset it stopped at. Two things make that offset
/// trustworthy, and both were found necessary by testing, not by reading:</para>
/// <list type="bullet">
/// <item><b>Each batch is sorted.</b> Re-issuing a cursor does <i>not</i> return its keys in the same order: Redis
/// rehashes a growing keyspace incrementally, a step per command, so our own reads between two pages reorder the batch.
/// A plain offset into Redis's order — what StackExchange.Redis's <c>IScanningCursor</c> does — repeated some baskets and
/// skipped others with nothing written at all.</item>
/// <item><b>The cursor carries a fingerprint of the batch.</b> Sorting fixes the order, not the membership: a basket
/// created or deleted in that batch's slots, or a rehash finishing between the two requests, changes which keys come
/// back. If the re-issued batch is not the set the offset was counted in, the batch is read again from its start. That
/// can repeat a basket, which <c>SCAN</c>'s contract allows; it cannot skip one that was there all along, which the
/// contract forbids.</item>
/// </list>
/// </summary>
internal sealed class RedisBasketAdminReader : IBasketAdminReader
{
    /// <summary>The <c>COUNT</c> hint of each <c>SCAN</c> call.</summary>
    internal const int ScanCount = 250;

    /// <summary>
    /// The most <c>SCAN</c> calls one request makes — about 5,000 keys examined. The keyspace also holds price-sync
    /// indexes, outbox lists and idempotency keys, which <c>MATCH</c> filters out only after scanning them; a request
    /// that runs out of calls returns what it found with a cursor to continue from.
    /// </summary>
    internal const int MaxScanCallsPerPage = 20;

    private readonly IDatabase _database;
    private readonly string _prefix;
    private readonly string _pattern;

    public RedisBasketAdminReader(IConnectionMultiplexer redis, IOptions<RedisBasketOptions> options)
    {
        _database = redis.GetDatabase();
        _prefix = options.Value.BasketKeyPrefix;
        _pattern = EscapeGlob(_prefix) + "*";
    }

    public async Task<StoredBasketPage> ScanBasketsAsync(
        BasketScanPosition from,
        int pageSize,
        DateTime? lastModifiedBefore,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var entries = new List<StoredBasketEntry>(pageSize);
        var position = from;

        for (var call = 0; call < MaxScanCallsPerPage; call++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (next, keys) = await ScanAsync(position.Cursor);
            var fingerprint = Fingerprint(keys);

            // Trusted only for the batch it was counted in (see the class comment); otherwise from the batch's start.
            var index = position.Offset > 0 && position.BatchFingerprint == fingerprint ? position.Offset : 0;

            while (index < keys.Length)
            {
                // Read in slices no larger than a page: a batch can hold far more keys than the page will use.
                var slice = keys[index..Math.Min(keys.Length, index + pageSize)];
                var values = await _database.StringGetAsync(slice);

                for (var i = 0; i < slice.Length; i++)
                {
                    index++;

                    // Deleted between the SCAN and the read — checked out or expired. Not a basket any more.
                    if (values[i].IsNullOrEmpty)
                    {
                        continue;
                    }

                    var userId = ((string)slice[i]!)[_prefix.Length..];
                    var basket = RedisBasketRepository.ReadBasket(values[i].ToString(), userId, NullLogger.Instance);

                    if (lastModifiedBefore is { } cutoff && (basket is null || basket.LastModifiedAt >= cutoff))
                    {
                        continue;
                    }

                    entries.Add(new StoredBasketEntry(userId, basket));

                    if (entries.Count == pageSize)
                    {
                        var resumeAt = index < keys.Length
                            ? new BasketScanPosition(position.Cursor, index, fingerprint)
                            : next == 0 ? (BasketScanPosition?)null : new BasketScanPosition(next, 0, 0);

                        return new StoredBasketPage(entries, resumeAt);
                    }
                }
            }

            if (next == 0)
            {
                return new StoredBasketPage(entries, Next: null);
            }

            position = new BasketScanPosition(next, 0, 0);
        }

        // Out of calls for this request: what was found, and where to carry on.
        return new StoredBasketPage(entries, position);
    }

    private async Task<(ulong Next, RedisKey[] Keys)> ScanAsync(ulong cursor)
    {
        var result = await _database.ExecuteAsync(
            "SCAN",
            cursor.ToString(CultureInfo.InvariantCulture),
            "MATCH",
            _pattern,
            "COUNT",
            ScanCount);

        var parts = (RedisResult[])result!;
        var next = ulong.Parse((string)parts[0]!, NumberStyles.None, CultureInfo.InvariantCulture);
        var keys = (RedisKey[])parts[1]!;

        // Ordinal, so the order depends on the keys alone and not on where Redis's rehash happens to be.
        Array.Sort(keys, (a, b) => string.CompareOrdinal(a, b));
        return (next, keys);
    }

    /// <summary>FNV-1a over the sorted batch: equal for the same set of keys, and almost surely different otherwise.</summary>
    private static ulong Fingerprint(RedisKey[] sortedKeys)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;
        foreach (var key in sortedKeys)
        {
            foreach (var b in Encoding.UTF8.GetBytes((string)key!))
            {
                hash = (hash ^ b) * prime;
            }

            // A separator, so ["ab", "c"] and ["a", "bc"] differ.
            hash = (hash ^ 0xFF) * prime;
        }

        return hash;
    }

    /// <summary>The prefix is configuration; a glob character in it must match itself, not act as a wildcard.</summary>
    private static string EscapeGlob(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '*' or '?' or '[' or ']' or '\\')
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }
}
