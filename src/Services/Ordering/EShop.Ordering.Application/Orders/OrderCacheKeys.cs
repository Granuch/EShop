namespace EShop.Ordering.Application.Orders;

/// <summary>
/// The one place Ordering's cache keys are spelled, for readers and writers alike.
///
/// <para>
/// Audit H4. Every write used to evict <c>orders:user:{id}</c> as an exact key, while the list it
/// meant was cached as <c>orders:user:{id}:p=..:ps=..:cur=..</c> — so a user's order list was never
/// invalidated by anything, and every eviction logged success. Spelling the key once removes that
/// class of drift; making the list a versioned family removes the need to name its pages at all.
/// </para>
/// </summary>
public static class OrderCacheKeys
{
    /// <summary>Exact key of GET /api/v1/orders/{id}.</summary>
    public static string Order(Guid orderId) => $"order:{orderId}";

    /// <summary>
    /// The versioned family (<c>IVersionedCacheKey</c>) of one user's order-list pages. A family, not
    /// a key: a page's key embeds its paging parameters, which no write can enumerate, so writes bump
    /// the family's version and every page becomes unreachable at once.
    /// </summary>
    public static string UserOrders(string userId) => $"orders:user:{userId}";
}
