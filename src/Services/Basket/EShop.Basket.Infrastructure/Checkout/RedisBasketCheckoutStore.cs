using EShop.Basket.Application.Abstractions;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Infrastructure.Configuration;
using EShop.Basket.Infrastructure.Outbox;
using EShop.BuildingBlocks.Messaging.Events;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Checkout;

/// <summary>
/// Redis implementation of <see cref="IBasketCheckoutStore"/> (Basket audit S3). The commit is one <c>MULTI/EXEC</c>
/// guarded by the stored basket's exact payload, so "basket gone" and "event queued" can only happen together.
/// </summary>
public sealed class RedisBasketCheckoutStore : IBasketCheckoutStore
{
    /// <summary>
    /// Basket audit D2. The marker is what lets a retry of a checkout that already went through get its checkoutId
    /// back; 24 hours covers any client's retry, and it costs one small key per user. It was 15 minutes and keyed
    /// every checkout in that window as a repeat (C2); now it is consulted only when the basket is gone.
    /// </summary>
    internal static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);

    private const string CompletedPrefix = "basket:checkout:completed:";
    private const string ProcessingPrefix = "basket:checkout:processing:";

    private readonly IDatabase _database;
    private readonly RedisBasketOptions _options;

    public RedisBasketCheckoutStore(IConnectionMultiplexer redis, IOptions<RedisBasketOptions> options)
    {
        _database = redis.GetDatabase();
        _options = options.Value;
    }

    public Task<bool> TryBeginProcessingAsync(string userId, TimeSpan ttl, CancellationToken cancellationToken = default)
        => _database.StringSetAsync(ProcessingPrefix + userId, DateTime.UtcNow.ToString("O"), ttl, when: When.NotExists);

    public Task ReleaseProcessingAsync(string userId, CancellationToken cancellationToken = default)
        => _database.KeyDeleteAsync(ProcessingPrefix + userId);

    public async Task<Guid?> GetCompletedCheckoutIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(CompletedPrefix + userId);
        return !value.IsNullOrEmpty && Guid.TryParse(value.ToString(), out var checkoutId) ? checkoutId : null;
    }

    public async Task<bool> CommitAsync(
        ShoppingBasket basket,
        BasketCheckedOutEvent checkoutEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(checkoutEvent);

        if (basket.ConcurrencyToken is null)
        {
            throw new InvalidOperationException(
                "Only a basket read from Redis can be checked out: its stored state is the commit's condition.");
        }

        var basketKey = _options.BasketKey(basket.UserId);
        var transaction = _database.CreateTransaction();

        // The WATCH. If anything rewrote the basket after checkout read it — an item added in another tab, a price
        // sync, a clear — EXEC discards every command below, and nothing is queued, deleted or marked.
        transaction.AddCondition(Condition.StringEqual(basketKey, basket.ConcurrencyToken));

        _ = transaction.ListLeftPushAsync(
            BasketOutboxKeys.Pending,
            RedisOutboxMessage.Serialize(checkoutEvent, checkoutEvent.CorrelationId));
        _ = transaction.KeyDeleteAsync(basketKey);

        foreach (var productId in basket.Items.Select(item => item.ProductId).Distinct())
        {
            _ = transaction.SetRemoveAsync(_options.ProductUsersKey(productId), basket.UserId);
        }

        _ = transaction.StringSetAsync(CompletedPrefix + basket.UserId, checkoutEvent.EventId.ToString("D"), CompletedTtl);

        return await transaction.ExecuteAsync();
    }
}
