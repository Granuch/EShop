using EShop.Catalog.Domain.Entities;

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

    /// <summary>One category's detail, <c>GET /api/v1/categories/{id}</c>.</summary>
    public static string Detail(Guid id) => $"category:{id}";

    /// <summary>
    /// M8 (Catalog audit Stage 8). The <i>other</i> detail entries that embed
    /// <paramref name="category"/> and so go stale when it changes: its parent's, which lists it in
    /// <c>ChildCategories</c>, and each child's, which carries its name as
    /// <c>ParentCategoryName</c>. Only the category's own key used to be evicted, so a parent's
    /// cached detail kept listing a renamed or deleted child for the full five-minute TTL.
    /// </summary>
    public static IEnumerable<string> RelativesOf(Category category)
    {
        if (category.ParentCategoryId is { } parentId)
            yield return Detail(parentId);

        foreach (var child in category.ChildCategories)
            yield return Detail(child.Id);
    }
}
