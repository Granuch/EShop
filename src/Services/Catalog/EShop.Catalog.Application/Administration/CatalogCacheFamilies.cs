using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Products;

namespace EShop.Catalog.Application.Administration;

/// <summary>
/// Every versioned cache family Catalog's queries write into — what <c>POST /api/v1/admin/cache/invalidate</c> can
/// reach (admin panel S19, endpoint #88).
///
/// <para>
/// <b>A family missing from this list is a family "invalidate all" silently skips.</b> The endpoint cannot discover
/// families at run time — <c>IDistributedCache</c> has no SCAN, which is why families exist at all — so this list is the
/// only place it learns them. <c>CatalogCacheFamiliesTests</c> reads the family of every <c>IVersionedCacheKey</c> query
/// in the assembly and fails when one is not here.
/// </para>
///
/// <para>
/// <b>Exact keys are out of reach, by construction.</b> A product's or category's detail entry lives under a key the
/// endpoint cannot name without the id, so it is not flushed here; it lapses on its own TTL (five to ten minutes), and
/// every write already evicts it by name. The endpoint's answer names the families it bumped, so nobody reads it as
/// "the whole cache is empty".
/// </para>
/// </summary>
public static class CatalogCacheFamilies
{
    public static readonly IReadOnlyList<string> All =
    [
        ProductCacheFamilies.ProductList,
        CategoryCacheFamilies.CategoryList
    ];
}
