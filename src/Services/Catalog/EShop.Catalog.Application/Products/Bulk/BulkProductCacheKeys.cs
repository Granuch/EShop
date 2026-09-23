namespace EShop.Catalog.Application.Products.Bulk;

/// <summary>What a bulk product command declares for <c>CacheInvalidationBehavior</c> to evict.</summary>
public static class BulkProductCacheKeys
{
    /// <summary>
    /// Both detail variants of every named product — or nothing, when the list is over the cap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exact keys per product are unavoidable: the detail read is not in a family, so a product left out here serves its
    /// old detail for the full TTL. The <c>products:list</c> family is declared separately and bumped <b>once</b>.
    /// </para>
    /// <para>
    /// <b>The over-cap case evicts nothing, on purpose.</b> <c>CacheInvalidationBehavior</c> runs after the pipeline
    /// returns whatever it returns, a validation refusal included, and removes each key with its own cache call. A
    /// request the validator refuses for naming too many ids changed nothing, so evicting its keys would only let one
    /// admin request turn into tens of thousands of cache round trips.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> DetailKeysFor(IEnumerable<Guid>? productIds)
    {
        var ids = productIds?.ToList() ?? [];
        return ids.Count <= BulkProductLimits.MaxItemsPerRequest
            ? ids.Distinct().SelectMany(ProductCacheKeys.AllDetailVariants)
            : [];
    }
}
