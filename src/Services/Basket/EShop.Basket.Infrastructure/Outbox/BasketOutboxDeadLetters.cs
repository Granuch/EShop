using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Outbox;

/// <summary>
/// The dead-letter list and the way back out of it (Basket audit S7, H6; D7). A dead-lettered checkout is the only
/// remaining record of an order — the basket was deleted when it was queued — so dead letters are kept until they are
/// replayed, never expired, and outbox health reports Degraded while any exist.
/// </summary>
public sealed class BasketOutboxDeadLetters
{
    /// <summary>
    /// KEYS: dead, pending. Moves the oldest dead letter to pending with its retry count reset to 0, in one step, so a
    /// crash cannot lose it between the two lists. Returns 1, or nil when there are none.
    /// </summary>
    private const string ReplayScript = """
        local payload = redis.call('RPOP', KEYS[1])
        if not payload then return false end
        local ok, message = pcall(cjson.decode, payload)
        if ok and type(message) == 'table' then
          message.retryCount = 0
          payload = cjson.encode(message)
        end
        redis.call('LPUSH', KEYS[2], payload)
        return 1
        """;

    private readonly IDatabase _database;

    public BasketOutboxDeadLetters(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public Task<long> CountAsync() => _database.ListLengthAsync(BasketOutboxKeys.DeadLetter);

    /// <summary>Replays up to <paramref name="max"/> dead letters, oldest first. Returns how many were replayed.</summary>
    public async Task<int> ReplayAsync(int max)
    {
        var replayed = 0;

        while (replayed < max)
        {
            var result = await _database.ScriptEvaluateAsync(
                ReplayScript, [BasketOutboxKeys.DeadLetter, BasketOutboxKeys.Pending]);

            if (result.IsNull)
            {
                break;
            }

            replayed++;
        }

        return replayed;
    }
}
