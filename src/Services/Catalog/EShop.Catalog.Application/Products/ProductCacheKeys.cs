namespace EShop.Catalog.Application.Products;

/// <summary>
/// The exact cache keys product reads write and product writes evict.
///
/// <para>
/// These were string interpolations repeated across a query and eight commands. That was survivable
/// while there was one key per resource, but D1 added a second <i>variant</i> of the detail key —
/// one for the public response and one that includes unpublished products — and a write that
/// evicts only one of them leaves the other serving stale data for its full TTL, silently. Naming
/// them here is what stops the two sides drifting: <see cref="AllDetailVariants"/> is the only
/// thing a command should declare.
/// </para>
/// </summary>
public static class ProductCacheKeys
{
    /// <summary>Detail response for a caller who may see only published products.</summary>
    public static string Detail(Guid productId) => $"product:{productId}";

    /// <summary>
    /// Detail response for an admin, who also sees drafts. A separate key because the same product
    /// yields 200 for an admin and 404 for everyone else while it is unpublished — sharing one key
    /// would serve whichever answer was cached first to both.
    /// </summary>
    public static string DetailIncludingUnpublished(Guid productId) => $"product:{productId}:unpub";

    /// <summary>
    /// Every detail variant for a product. <b>Declare this from a command, not a single key</b> —
    /// missing a variant is invisible until someone reads the stale one.
    /// </summary>
    public static IEnumerable<string> AllDetailVariants(Guid productId) =>
    [
        Detail(productId),
        DetailIncludingUnpublished(productId)
    ];

    /// <summary>
    /// Product list for one category. Single-variant on purpose — see
    /// <c>GetProductByCategoryQuery.CacheKey</c> for why that endpoint does not vary by role.
    /// </summary>
    public static string Category(Guid categoryId) => $"products:category:{categoryId}";
}
