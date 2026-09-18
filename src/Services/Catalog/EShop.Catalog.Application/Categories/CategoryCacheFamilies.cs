namespace EShop.Catalog.Application.Categories;

/// <summary>
/// DEBT-16 / A4 (Admin panel S5). Names the versioned cache-key families for categories, the same
/// way <c>ProductCacheFamilies</c> does for products.
/// </summary>
public static class CategoryCacheFamilies
{
    /// <summary>
    /// The category tree read, <c>GET /api/v1/categories</c>.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> The key used to be the fixed string <c>categories:all</c>,
    /// evicted by exact name from three command handlers. S5 added <c>?includeInactive=true</c>,
    /// which makes the key a family rather than a constant — and the naive version of that change
    /// is a data leak: leave the flag out of the key and an admin's request populates
    /// <c>categories:all</c> with deactivated categories, which every anonymous caller then reads
    /// for the full ten-minute TTL. Put the flag in the key but leave the exact-string evictors
    /// alone, and the admin variant is never evicted by any write — stale for its full TTL, with
    /// the eviction logging success.
    /// </para>
    /// <para>
    /// A version family fixes both at once: the key embeds the flag, and one bump makes every
    /// variant unreachable without anyone naming them. The exact-key <c>CategoryCacheKeys.All</c>
    /// evictions were removed from the create/update/delete commands in the same change — leaving
    /// them would evict a key nothing writes any more, which removes nothing and logs success.
    /// Detail keys are unaffected: they are nameable, so they stay on exact-key eviction.
    /// </para>
    /// </summary>
    public const string CategoryList = "categories:list";
}
