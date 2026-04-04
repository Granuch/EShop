using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Idempotency;

public class RedisMessageIdempotencyStore
{
    private const string ProcessedPrefix = "basket:consumer:processed:";
    private const string ProcessingPrefix = "basket:consumer:processing:";

    private readonly IDatabase _database;

    public RedisMessageIdempotencyStore(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public virtual Task<bool> TryMarkProcessedAsync(Guid messageId, TimeSpan ttl)
    {
        var key = $"{ProcessedPrefix}{messageId}";
        return _database.StringSetAsync(key, DateTime.UtcNow.ToString("O"), ttl, when: When.NotExists);
    }

    public virtual Task<bool> IsProcessedAsync(Guid messageId)
    {
        var key = $"{ProcessedPrefix}{messageId}";
        return _database.KeyExistsAsync(key);
    }

    public virtual Task<bool> TryBeginProcessingAsync(Guid messageId, TimeSpan ttl)
    {
        var key = $"{ProcessingPrefix}{messageId}";
        return _database.StringSetAsync(key, DateTime.UtcNow.ToString("O"), ttl, when: When.NotExists);
    }

    public virtual Task CompleteProcessingAsync(Guid messageId)
    {
        var key = $"{ProcessingPrefix}{messageId}";
        return _database.KeyDeleteAsync(key);
    }
}
