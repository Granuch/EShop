using EShop.Basket.Application.Abstractions;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Idempotency;

public class RedisCheckoutIdempotencyStore : ICheckoutIdempotencyStore
{
    private const string CompletedPrefix = "basket:checkout:completed:";
    private const string ProcessingPrefix = "basket:checkout:processing:";

    private readonly IDatabase _database;

    public RedisCheckoutIdempotencyStore(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public async Task<Guid?> GetCompletedCheckoutIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(GetCompletedKey(userId));
        return value.IsNullOrEmpty || !Guid.TryParse(value.ToString(), out var checkoutId)
            ? null
            : checkoutId;
    }

    public Task<bool> TryBeginProcessingAsync(string userId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return _database.StringSetAsync(GetProcessingKey(userId), DateTime.UtcNow.ToString("O"), ttl, when: When.NotExists);
    }

    public Task MarkCompletedAsync(string userId, Guid checkoutId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        return _database.StringSetAsync(GetCompletedKey(userId), checkoutId.ToString("D"), ttl);
    }

    public Task ReleaseProcessingAsync(string userId, CancellationToken cancellationToken = default)
    {
        return _database.KeyDeleteAsync(GetProcessingKey(userId));
    }

    private static string GetCompletedKey(string userId) => $"{CompletedPrefix}{userId}";

    private static string GetProcessingKey(string userId) => $"{ProcessingPrefix}{userId}";
}
