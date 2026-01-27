using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using StackExchange.Redis;
using System.Text.Json;

namespace EShop.Basket.Infrastructure.Repositories;

/// <summary>
/// Redis-based basket repository
/// </summary>
public class RedisBasketRepository : IBasketRepository
{
    // TODO: Inject IConnectionMultiplexer, ILogger
    // private readonly IConnectionMultiplexer _redis;
    // private readonly IDatabase _database;
    // private readonly ILogger<RedisBasketRepository> _logger;

    public async Task<ShoppingBasket?> GetBasketAsync(string userId, CancellationToken cancellationToken = default)
    {
        // TODO: Get basket JSON from Redis by key "basket:{userId}"
        // TODO: Deserialize to ShoppingBasket
        // TODO: Return null if not found
        throw new NotImplementedException();
    }

    public async Task<ShoppingBasket> SaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default)
    {
        // TODO: Serialize basket to JSON
        // TODO: Save to Redis with key "basket:{userId}"
        // TODO: Set TTL to 7 days
        // TODO: Return saved basket
        throw new NotImplementedException();
    }

    public async Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default)
    {
        // TODO: Delete basket from Redis by key "basket:{userId}"
        // TODO: Return true if deleted, false if not found
        throw new NotImplementedException();
    }
}
