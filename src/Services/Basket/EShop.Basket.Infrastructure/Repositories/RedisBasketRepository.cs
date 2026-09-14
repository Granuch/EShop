using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Configuration;
using EShop.BuildingBlocks.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace EShop.Basket.Infrastructure.Repositories;

/// <summary>
/// Redis-based basket repository.
///
/// <para><b>Every write is one <c>MULTI/EXEC</c> conditioned on the stored state it replaces (Basket audit S4, H3/M8).</b>
/// The condition is the exact payload the basket was read from (<see cref="ShoppingBasket.ConcurrencyToken"/>), and the
/// reverse-index changes are computed from that same payload, inside the same transaction. The save used to be an
/// unconditional <c>SET</c> whose index diff came from a read taken outside the transaction, so concurrent writes lost
/// each other's items and dropped users from price sync's index.</para>
///
/// <para><b>An unreadable document is kept, not overwritten (Basket audit L6, S9).</b> A document that does not
/// deserialize, belongs to another user or holds a line the domain refuses reads as "no basket", as before. A write that
/// replaces or deletes it now first pushes it onto <see cref="RedisBasketOptions.CorruptBasketKey"/>, in the same
/// transaction, and logs an error; it used to vanish with only a warning to show it had existed.</para>
/// </summary>
public class RedisBasketRepository : IBasketRepository
{
    private const int DeleteAttempts = 5;

    /// <summary>How many replaced unreadable documents are kept per user; the oldest beyond this are dropped.</summary>
    private const int CorruptDocumentsKept = 10;

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

        var value = await _database.StringGetAsync(GetBasketKey(userId));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return ReadBasket(value.ToString(), userId);
    }

    public async Task<bool> TrySaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var basketKey = GetBasketKey(basket.UserId);

        // The condition and the index diff come from one place: the stored state this basket was read from.
        Condition condition;
        BasketDocument? previousDocument = null;
        var unreadable = RedisValue.Null;

        if (basket.ConcurrencyToken is { } token)
        {
            condition = Condition.StringEqual(basketKey, token);
            previousDocument = TryDeserialize(token, basket.UserId);
        }
        else if (await NoReadableBasketAsync(basketKey, basket.UserId) is { } noBasket)
        {
            condition = noBasket.Condition;
            unreadable = noBasket.Unreadable;
        }
        else
        {
            // This basket was read as missing, and a readable one has been stored since.
            return false;
        }

        var document = BasketDocument.FromBasket(basket);
        var payload = JsonSerializer.Serialize(document, JsonOptions);

        var transaction = _database.CreateTransaction();
        transaction.AddCondition(condition);

        if (!unreadable.IsNull)
        {
            Keep(transaction, basket.UserId, unreadable);
        }

        _ = transaction.StringSetAsync(basketKey, payload, _options.BasketTtl);

        var previousProductIds = previousDocument?.Items.Select(i => i.ProductId).ToHashSet() ?? [];
        var currentProductIds = document.Items.Select(i => i.ProductId).ToHashSet();

        foreach (var addedProductId in currentProductIds.Except(previousProductIds))
        {
            _ = transaction.SetAddAsync(GetProductUsersKey(addedProductId), basket.UserId);
        }

        foreach (var removedProductId in previousProductIds.Except(currentProductIds))
        {
            _ = transaction.SetRemoveAsync(GetProductUsersKey(removedProductId), basket.UserId);
        }

        if (!await transaction.ExecuteAsync())
        {
            _logger.LogDebug(
                "Basket for user {UserId} changed after it was read; the save was not applied",
                basket.UserId);
            return false;
        }

        if (!unreadable.IsNull)
        {
            LogKept(basket.UserId, "replaced");
        }

        basket.MarkStored(payload);

        // TTL refresh is best-effort and does not require MULTI/EXEC atomicity.
        // Run as pipelined async operations after commit to avoid bloating transaction payload.
        var ttlRefreshTasks = currentProductIds
            .Select(productId => _database.KeyExpireAsync(GetProductUsersKey(productId), _options.BasketTtl));

        await Task.WhenAll(ttlRefreshTasks);

        return true;
    }

    public Task<bool> TryDeleteBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var token = basket.ConcurrencyToken
            ?? throw new InvalidOperationException("Only a basket read from Redis can be deleted conditionally.");

        return DeleteIfUnchangedAsync(basket.UserId, token);
    }

    public async Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        var basketKey = GetBasketKey(userId);

        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            var existing = await _database.StringGetAsync(basketKey);
            if (existing.IsNullOrEmpty)
            {
                return false;
            }

            // Conditioned on what was read, so the index entries removed are exactly those of the basket deleted: an
            // item added meanwhile used to survive in the index after its basket was gone (M8).
            if (await DeleteIfUnchangedAsync(userId, existing.ToString()))
            {
                return true;
            }
        }

        throw new InvalidOperationException($"The basket for user '{userId}' kept changing while it was being deleted.");
    }

    public async Task<bool> TryRemoveFromProductIndexAsync(
        Guid productId,
        string userId,
        ShoppingBasket? asRead,
        CancellationToken cancellationToken = default)
    {
        var basketKey = GetBasketKey(userId);

        Condition condition;
        if (asRead?.ConcurrencyToken is { } token)
        {
            condition = Condition.StringEqual(basketKey, token);
        }
        else if (await NoReadableBasketAsync(basketKey, userId) is { } noBasket)
        {
            // An unreadable document stays where it is: only a write that replaces it moves it aside.
            condition = noBasket.Condition;
        }
        else
        {
            return false;
        }

        var transaction = _database.CreateTransaction();
        transaction.AddCondition(condition);
        _ = transaction.SetRemoveAsync(GetProductUsersKey(productId), userId);

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

    /// <summary>
    /// Deletes the basket and unindexes the products of <paramref name="storedPayload"/>, if that is still exactly what
    /// is stored. An unreadable payload has no products to unindex, and is kept instead (L6).
    /// </summary>
    private async Task<bool> DeleteIfUnchangedAsync(string userId, string storedPayload)
    {
        var basketKey = GetBasketKey(userId);
        var stored = ReadBasket(storedPayload, userId);

        var transaction = _database.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(basketKey, storedPayload));

        if (stored == null)
        {
            Keep(transaction, userId, storedPayload);
        }

        _ = transaction.KeyDeleteAsync(basketKey);

        foreach (var productId in stored?.Items.Select(item => item.ProductId) ?? [])
        {
            _ = transaction.SetRemoveAsync(GetProductUsersKey(productId), userId);
        }

        if (!await transaction.ExecuteAsync())
        {
            return false;
        }

        if (stored == null)
        {
            LogKept(userId, "deleted");
        }

        return true;
    }

    /// <summary>
    /// When there is still no basket <see cref="GetBasketAsync"/> would return: the key is absent, or it still holds the
    /// same unreadable document, which is then returned as <c>Unreadable</c> so the write replacing it can keep it (L6).
    /// <c>null</c> if a readable basket is stored now.
    /// </summary>
    private async Task<NoReadableBasket?> NoReadableBasketAsync(string basketKey, string userId)
    {
        var existing = await _database.StringGetAsync(basketKey);
        if (existing.IsNullOrEmpty)
        {
            return new NoReadableBasket(Condition.KeyNotExists(basketKey), RedisValue.Null);
        }

        return ReadBasket(existing.ToString(), userId) == null
            ? new NoReadableBasket(Condition.StringEqual(basketKey, existing), existing)
            : null;
    }

    /// <summary>Pushes an unreadable document onto the user's corrupt list, inside the write that removes it.</summary>
    private void Keep(ITransaction transaction, string userId, RedisValue unreadablePayload)
    {
        var key = _options.CorruptBasketKey(userId);
        _ = transaction.ListLeftPushAsync(key, unreadablePayload);
        _ = transaction.ListTrimAsync(key, 0, CorruptDocumentsKept - 1);
        _ = transaction.KeyExpireAsync(key, _options.CorruptBasketRetention);
    }

    private void LogKept(string userId, string action)
    {
        _logger.LogError(
            "An unreadable basket document for user {UserId} was {Action}. It is kept under {CorruptBasketKey} for {Retention}",
            userId,
            action,
            _options.CorruptBasketKey(userId),
            _options.CorruptBasketRetention);
    }

    private string GetBasketKey(string userId) => _options.BasketKey(userId);

    private string GetProductUsersKey(Guid productId) => _options.ProductUsersKey(productId);

    /// <summary>
    /// The basket in <paramref name="payload"/>, if it deserializes, belongs to <paramref name="userId"/> and holds only
    /// lines the domain accepts; otherwise <c>null</c>. A line the domain refused used to throw from every read, so that
    /// user's GET and every write failed until the basket expired.
    /// </summary>
    private ShoppingBasket? ReadBasket(string payload, string userId)
    {
        var document = TryDeserialize(payload, userId);
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

        try
        {
            return document.ToBasket(payload);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex,
                "Basket document for user {UserId} holds a line the domain refuses. Treating record as corrupted.",
                userId);
            return null;
        }
    }

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

    private readonly record struct NoReadableBasket(Condition Condition, RedisValue Unreadable);

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
                        Quantity = item.Quantity,
                        AddedAt = item.CreatedAt
                    })
                    .ToList()
            };
        }

        public ShoppingBasket ToBasket(string payload)
        {
            var createdAt = CreatedAt == default ? DateTime.UtcNow : CreatedAt;
            var lastModifiedAt = LastModifiedAt == default ? createdAt : LastModifiedAt;

            return ShoppingBasket.Rehydrate(
                UserId,
                createdAt,
                lastModifiedAt,
                (Items ?? [])
                    .Select(item => new StoredBasketItem(item.ProductId, item.ProductName, item.Price, item.Quantity, item.AddedAt))
                    .ToArray(),
                concurrencyToken: payload);
        }
    }

    private sealed class BasketItemDocument
    {
        public Guid ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public int Quantity { get; init; }

        /// <summary>When the line was added (Basket audit L1, S9); absent from documents written before it.</summary>
        public DateTime? AddedAt { get; init; }
    }
}
