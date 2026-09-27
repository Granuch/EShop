namespace EShop.Catalog.Application.Categories;

/// <summary>
/// Category cache keys, built in one place. The Stage 6 lesson applies: when a key is written in one
/// file and evicted by a string built in another, the two drift apart, and the eviction then removes
/// nothing while logging success.
/// </summary>
public static class CategoryCacheKeys
{
    /// <summary>
    /// <b>Dead as of Admin panel S5 (A4) — do not use it, and do not "restore" an eviction of it.</b>
    /// The tree read's key became <c>categories:list:inactive={bool}</c> when
    /// <c>?includeInactive=</c> was added, so nothing writes this string any more and evicting it
    /// removes nothing while logging success. The replacement is the versioned family
    /// <see cref="CategoryCacheFamilies.CategoryList"/>, which every category write bumps.
    /// Kept only so that a branch still naming it fails to compile rather than silently no-opping.
    /// </summary>
    [Obsolete("Superseded by CategoryCacheFamilies.CategoryList (A4, Admin panel S5): nothing writes this key any more, so evicting it is a silent no-op.")]
    public const string All = "categories:all";

    /// <summary>
    /// One category's detail, <c>GET /api/v1/categories/{id}</c> — the <i>base</i> key only.
    /// </summary>
    /// <remarks>
    /// F-37 (frontend-contracts R5): <b>do not evict this by exact key.</b> The detail now carries the
    /// category's whole subtree, so a write to any descendant makes every ancestor's entry stale — a
    /// set no command can name. The query therefore joined the versioned family
    /// <see cref="CategoryCacheFamilies.CategoryList"/>, which every category write bumps, and its
    /// stored key embeds the family version. An exact-key eviction built from this string would match
    /// nothing and log success — which is why the M8 <c>RelativesOf</c> evictions (parent and children
    /// only) were deleted in the same change rather than kept "for safety".
    /// </remarks>
    public static string Detail(Guid id) => $"category:{id}";
}
