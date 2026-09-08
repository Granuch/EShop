namespace EShop.Catalog.Application.Products;

/// <summary>
/// DEBT-16. Names the versioned cache-key families in this service.
///
/// <para>
/// A family is shared by the query that reads it and every command that must evict it, and those
/// live in different folders — so a literal string would have to match across five files with
/// nothing checking it. A typo would not fail: the query would keep reading one family while the
/// writes bumped another, which is indistinguishable from the stale-list bug this was written to
/// fix.
/// </para>
/// </summary>
public static class ProductCacheFamilies
{
    /// <summary>
    /// Every <c>products:list:*</c> result. Bumped by any write that can change which products
    /// appear in a list, their order, or the fields the list projects.
    /// </summary>
    public const string ProductList = "products:list";
}
