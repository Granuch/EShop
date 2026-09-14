using EShop.Basket.Domain.Entities;

namespace EShop.Basket.Domain.Interfaces;

/// <summary>
/// Repository interface for basket storage (Redis-based).
///
/// <para><b>Writes are conditional (Basket audit S4, H3).</b> A basket carries the stored state it was read from
/// (<see cref="ShoppingBasket.ConcurrencyToken"/>), and the <c>Try*</c> writes apply only if that is still what is
/// stored. A <c>false</c> means another write got there first and nothing was written: read again and redo the change.
/// There is no unconditional save — last-write-wins silently dropped items and undid price syncs.</para>
/// </summary>
public interface IBasketRepository
{
    Task<ShoppingBasket?> GetBasketAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the basket, with its reverse-index changes, only if the stored basket is still the one it was read from
    /// (or, for a basket that was never read, if none is stored). On success the basket's token becomes what was stored.
    /// </summary>
    Task<bool> TrySaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default);

    /// <summary>Deletes the basket and its reverse-index entries only if it is still stored exactly as it was read.</summary>
    Task<bool> TryDeleteBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes whatever basket the user has, with its reverse-index entries, retrying if it changes meanwhile.
    /// False if there was none.
    /// </summary>
    Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops <paramref name="userId"/> from the product's reverse index, only if the user's stored basket is still
    /// <paramref name="asRead"/> (<c>null</c>: still no basket) — so an entry added by a concurrent write is kept.
    /// </summary>
    Task<bool> TryRemoveFromProductIndexAsync(
        Guid productId,
        string userId,
        ShoppingBasket? asRead,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetUsersContainingProductAsync(Guid productId, CancellationToken cancellationToken = default);
}
