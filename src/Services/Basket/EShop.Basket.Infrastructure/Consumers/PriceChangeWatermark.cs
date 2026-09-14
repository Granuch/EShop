using EShop.Basket.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Consumers;

/// <summary>
/// Basket audit S9 (M10, D9). The time of the newest price change applied for each product, taken from the event's
/// <c>OccurredOn</c>. An older event that arrives later — out of order, or redelivered after a newer one — is recognised
/// and skipped, where it used to put the older price back. Catalog raises one event per change, so the newest event
/// carries the current price, and a missed event is repaired by the next one.
///
/// <para>Kept per product, not per basket line: an event older than the newest is stale for every basket, and deciding
/// that once costs one round trip instead of a write to every basket. The alternative the audit offered — apply an event
/// only while the basket's price equals its <c>OldPrice</c> — leaves a basket at a stale price for good once one event is
/// missed.</para>
/// </summary>
public class PriceChangeWatermark
{
    /// <summary>Far longer than any redelivery or retry window; every newer event starts it again.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    // Microseconds since the Unix epoch, so Lua's double-precision numbers compare them exactly; DateTime ticks are
    // beyond 2^53 and would not be.
    private const string AdvanceScript = """
        local current = redis.call('GET', KEYS[1])
        if current and tonumber(current) > tonumber(ARGV[1]) then
          return 0
        end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        return 1
        """;

    private readonly IDatabase _database;
    private readonly RedisBasketOptions _options;

    public PriceChangeWatermark(IConnectionMultiplexer redis, IOptions<RedisBasketOptions> options)
    {
        _database = redis.GetDatabase();
        _options = options.Value;
    }

    /// <summary>
    /// Records a change that happened at <paramref name="occurredOn"/> as the product's newest; false, recording nothing,
    /// if a newer change was already recorded. A change at the same time is not older, so a redelivered event proceeds.
    /// </summary>
    public virtual async Task<bool> TryAdvanceAsync(Guid productId, DateTime occurredOn)
    {
        var result = await _database.ScriptEvaluateAsync(
            AdvanceScript,
            [(RedisKey)_options.ProductPriceChangedAtKey(productId)],
            [ToMicroseconds(occurredOn), (long)Retention.TotalMilliseconds]);

        return (long)result == 1;
    }

    /// <summary>Whether a change newer than <paramref name="occurredOn"/> has been recorded for the product.</summary>
    public virtual async Task<bool> IsSupersededAsync(Guid productId, DateTime occurredOn)
    {
        var newest = await _database.StringGetAsync(_options.ProductPriceChangedAtKey(productId));
        return newest.TryParse(out long newestMicroseconds) && newestMicroseconds > ToMicroseconds(occurredOn);
    }

    private static long ToMicroseconds(DateTime occurredOn)
    {
        var utc = occurredOn.Kind == DateTimeKind.Local ? occurredOn.ToUniversalTime() : occurredOn;
        return (utc.Ticks - DateTime.UnixEpoch.Ticks) / 10;
    }
}
