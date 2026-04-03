using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Idempotency;

public class RedisMessageIdempotencyStore
{
    private const string ProcessedPrefix = "basket:consumer:processed:";

    private readonly IDatabase _database;

    public RedisMessageIdempotencyStore(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public Task<bool> TryMarkProcessedAsync(Guid messageId, TimeSpan ttl)
    {
        var key = $"{ProcessedPrefix}{messageId}";
        return _database.StringSetAsync(key, DateTime.UtcNow.ToString("O"), ttl, when: When.NotExists);
    }
}
