using EShop.Basket.Domain.Entities;

namespace EShop.Basket.Domain.Interfaces;

/// <summary>
/// The admin panel's view over every stored basket (Admin panel S14, #78/#79).
///
/// <para><b>It walks Redis with <c>SCAN</c>, never <c>KEYS</c>.</b> <c>KEYS</c> blocks the server for the whole keyspace;
/// <c>SCAN</c> does a bounded amount of work per call and hands back a cursor. A page therefore carries a
/// <see cref="BasketScanPosition"/> to resume from, and <c>SCAN</c>'s own guarantees apply to it: a basket stored for the
/// whole walk is returned at least once — occasionally twice, so a caller collecting a whole walk should de-duplicate
/// by user id — one created or deleted during it may or may not be, and the order is not a sort.</para>
/// </summary>
public interface IBasketAdminReader
{
    /// <summary>
    /// Up to <paramref name="pageSize"/> baskets from <paramref name="from"/>; with <paramref name="lastModifiedBefore"/>,
    /// only readable baskets last changed strictly before it. A page may be short, or even empty, while
    /// <see cref="StoredBasketPage.Next"/> is still set: each request does a bounded amount of scanning.
    /// </summary>
    Task<StoredBasketPage> ScanBasketsAsync(
        BasketScanPosition from,
        int pageSize,
        DateTime? lastModifiedBefore,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Where a basket walk resumes: the <c>SCAN</c> cursor, how many keys of that cursor's batch (sorted) were already
/// returned, and a fingerprint of the batch they were counted in — so a page can stop part-way through a batch without
/// dropping the rest of it, and the offset is trusted only while the batch is still the same set of keys.
/// </summary>
public readonly record struct BasketScanPosition(ulong Cursor, int Offset, ulong BatchFingerprint)
{
    public static BasketScanPosition Start => new(0, 0, 0);
}

/// <summary>A stored basket; <see cref="Basket"/> is null when the document cannot be read.</summary>
public sealed record StoredBasketEntry(string UserId, ShoppingBasket? Basket);

/// <summary>One page of a basket walk. <see cref="Next"/> is null once the walk has covered the whole keyspace.</summary>
public sealed record StoredBasketPage(IReadOnlyList<StoredBasketEntry> Entries, BasketScanPosition? Next);
