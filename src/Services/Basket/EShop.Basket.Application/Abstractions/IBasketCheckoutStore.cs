using EShop.Basket.Domain.Entities;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Basket.Application.Abstractions;

/// <summary>
/// Checkout's persistence (Basket audit S3). It replaced <c>ICheckoutIdempotencyStore</c> and the fire-and-forget
/// <c>BasketRedisOutbox</c>, whose separate writes let a checkout report success with no event stored (H1) or queue an
/// event behind a 400 that invited a duplicate order (H2).
/// </summary>
public interface IBasketCheckoutStore
{
    /// <summary>Takes the user's short checkout lock; false if another checkout holds it.</summary>
    Task<bool> TryBeginProcessingAsync(string userId, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task ReleaseProcessingAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>The id of the user's last completed checkout, kept for 24 hours (D2); null if there is none.</summary>
    Task<Guid?> GetCompletedCheckoutIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one Redis transaction: queues <paramref name="checkoutEvent"/> in the outbox, deletes the basket and its
    /// reverse-index entries, and records the event's id as the user's completed checkout. It applies only if the
    /// stored basket is still exactly the one <paramref name="basket"/> was read from; otherwise it returns false
    /// having written nothing.
    /// </summary>
    Task<bool> CommitAsync(ShoppingBasket basket, BasketCheckedOutEvent checkoutEvent, CancellationToken cancellationToken = default);
}
