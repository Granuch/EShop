using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace EShop.Basket.Infrastructure.Repositories;

/// <summary>
/// Redis-based basket repository
/// </summary>
public class RedisBasketRepository : IBasketRepository
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisBasketRepository> _logger;
    private readonly RedisBasketOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisBasketRepository(
        IConnectionMultiplexer redis,
        IOptions<RedisBasketOptions> options,
        ILogger<RedisBasketRepository> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ShoppingBasket?> GetBasketAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        var key = GetBasketKey(userId);
        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var document = TryDeserialize(value.ToString(), userId);
        if (document == null)
        {
            return null;
        }

        if (!string.Equals(document.UserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Basket payload user mismatch for key user {RequestedUserId}. Document contains user {DocumentUserId}. Treating record as corrupted.",
                userId,
                document.UserId);
            return null;
        }

        var createdAt = document.CreatedAt == default ? DateTime.UtcNow : document.CreatedAt;
        var lastModifiedAt = document.LastModifiedAt == default ? createdAt : document.LastModifiedAt;

        return ShoppingBasket.Rehydrate(
            document.UserId,
            createdAt,
            lastModifiedAt,
            document.Items
                .Select(item => (item.ProductId, item.ProductName, item.Price, item.Quantity))
                .ToArray());
    }

    public async Task<ShoppingBasket> SaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var basketKey = GetBasketKey(basket.UserId);

        // Best-effort reverse index synchronization with last-write-wins semantics.
        var previous = await _database.StringGetAsync(basketKey);
        var previousDocument = previous.IsNullOrEmpty
            ? null
            : TryDeserialize(previous.ToString(), basket.UserId);

        var document = BasketDocument.FromBasket(basket);
        var payload = JsonSerializer.Serialize(document, JsonOptions);

        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(basketKey, payload, _options.BasketTtl);

        var previousProductIds = previousDocument?.Items.Select(i => i.ProductId).ToHashSet() ?? [];
        var currentProductIds = document.Items.Select(i => i.ProductId).ToHashSet();

        foreach (var addedProductId in currentProductIds.Except(previousProductIds))
        {
            var productUsersKey = GetProductUsersKey(addedProductId);
            _ = transaction.SetAddAsync(productUsersKey, basket.UserId);
        }

        foreach (var removedProductId in previousProductIds.Except(currentProductIds))
        {
            var productUsersKey = GetProductUsersKey(removedProductId);
            _ = transaction.SetRemoveAsync(productUsersKey, basket.UserId);
        }

        var committed = await transaction.ExecuteAsync();
        if (!committed)
        {
            _logger.LogWarning("Redis transaction failed when saving basket for user {UserId}", basket.UserId);
            throw new InvalidOperationException($"Failed to save basket for user '{basket.UserId}'.");
        }

        // TTL refresh is best-effort and does not require MULTI/EXEC atomicity.
        // Run as pipelined async operations after commit to avoid bloating transaction payload.
        var ttlRefreshTasks = currentProductIds
            .Select(productId => _database.KeyExpireAsync(GetProductUsersKey(productId), _options.BasketTtl));

        await Task.WhenAll(ttlRefreshTasks);

        return basket;
    }

    public async Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        var basketKey = GetBasketKey(userId);
        var existing = await _database.StringGetAsync(basketKey);
        if (existing.IsNullOrEmpty)
        {
            return false;
        }

        var document = TryDeserialize(existing.ToString(), userId);

        var transaction = _database.CreateTransaction();
        _ = transaction.KeyDeleteAsync(basketKey);

        if (document != null)
        {
            foreach (var item in document.Items)
            {
                _ = transaction.SetRemoveAsync(GetProductUsersKey(item.ProductId), userId);
            }
        }

        return await transaction.ExecuteAsync();
    }

    public async Task<IReadOnlyCollection<string>> GetUsersContainingProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var members = await _database.SetMembersAsync(GetProductUsersKey(productId));
        return members
            .Where(v => !v.IsNullOrEmpty)
            .Select(v => v.ToString())
            .ToArray();
    }

    private string GetBasketKey(string userId) => $"{_options.BasketKeyPrefix}{userId}";

    private string GetProductUsersKey(Guid productId) => $"{_options.ProductUsersKeyPrefix}{productId}:users";

    private BasketDocument? TryDeserialize(string payload, string userId)
    {
        try
        {
            return JsonSerializer.Deserialize<BasketDocument>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize basket document for user {UserId}", userId);
            return null;
        }
    }

    private sealed class BasketDocument
    {
        public string UserId { get; init; } = string.Empty;
        public List<BasketItemDocument> Items { get; init; } = [];
        public DateTime CreatedAt { get; init; }
        public DateTime LastModifiedAt { get; init; }

        public static BasketDocument FromBasket(ShoppingBasket basket)
        {
            return new BasketDocument
            {
                UserId = basket.UserId,
                CreatedAt = basket.CreatedAt,
                LastModifiedAt = basket.LastModifiedAt,
                Items = basket.Items
                    .Select(item => new BasketItemDocument
                    {
                        ProductId = item.ProductId,
                        ProductName = item.ProductName,
                        Price = item.Price,
                        Quantity = item.Quantity
                    })
                    .ToList()
            };
        }
    }

    private sealed class BasketItemDocument
    {
        public Guid ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public int Quantity { get; init; }
    }
}
