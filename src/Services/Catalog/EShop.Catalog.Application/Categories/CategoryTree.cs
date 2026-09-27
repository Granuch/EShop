using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Application.Categories;

/// <summary>
/// Builds category DTO trees from one flat read (F-37, frontend-contracts R5).
/// </summary>
/// <remarks>
/// <para>
/// The tree used to come from EF <c>Include</c>/<c>ThenInclude</c>, which loads exactly as many
/// levels as it names: <c>GET /categories</c> stopped at grandchildren and cut the roots at 100, and
/// <c>GET /categories/{id}</c> showed one level of children whose own <c>childCategories</c> were
/// always <c>[]</c>. Categories could be nested deeper (create with a parent, and move), so a
/// fourth-level category simply vanished and its parent looked like a leaf. A client could not tell
/// a leaf from a truncated node.
/// </para>
/// <para>
/// Building in memory from every row has no depth limit and costs one query. Categories are few
/// (a navigation structure, not a data set), and both reads are cached, so reading the whole table
/// is cheaper than a recursive query per request.
/// </para>
/// <para>
/// A category whose parent is not in <paramref name="categories"/> is unreachable and left out,
/// exactly as it was unreachable from the roots before. Under the <c>IsActive</c> filter that cannot
/// normally happen: delete refuses while live children remain, restore refuses under a deleted
/// parent, and a move to a deleted parent is <c>Category.ParentNotFound</c>.
/// </para>
/// </remarks>
public static class CategoryTree
{
    /// <summary>
    /// Every root in <paramref name="categories"/> with its full subtree. Siblings keep the order the
    /// input had, so pass them ordered (the repository orders by DisplayOrder, Name, Id).
    /// </summary>
    public static List<CategoryDto> Roots(IReadOnlyList<Category> categories)
    {
        var index = new TreeIndex(categories);
        return index.ChildrenOf(null).Select(index.Build).ToList();
    }

    /// <summary>
    /// <paramref name="id"/> with its full subtree, or <c>null</c> when it is not in
    /// <paramref name="categories"/>.
    /// </summary>
    public static CategoryDto? Subtree(IReadOnlyList<Category> categories, Guid id)
    {
        var index = new TreeIndex(categories);
        return index.Find(id) is { } category ? index.Build(category) : null;
    }

    private sealed class TreeIndex
    {
        private readonly Dictionary<Guid, Category> _byId;
        private readonly ILookup<Guid?, Category> _byParent;

        // A cycle cannot be written (Category.MoveTo checks the persisted chain), and a cycle is
        // unreachable from a root anyway. The set is for the one caller that starts below the roots,
        // Subtree, so that corrupt data yields a finite tree instead of a stack overflow.
        private readonly HashSet<Guid> _visited = [];

        public TreeIndex(IReadOnlyList<Category> categories)
        {
            _byId = categories.ToDictionary(c => c.Id);
            _byParent = categories.ToLookup(c => c.ParentCategoryId);
        }

        public Category? Find(Guid id) => _byId.GetValueOrDefault(id);

        public IEnumerable<Category> ChildrenOf(Guid? parentId) => _byParent[parentId];

        public CategoryDto Build(Category category)
        {
            _visited.Add(category.Id);

            return new CategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                Slug = category.Slug,
                ParentCategoryId = category.ParentCategoryId,
                ParentCategoryName = category.ParentCategoryId is { } parentId
                    ? _byId.GetValueOrDefault(parentId)?.Name
                    : null,
                DisplayOrder = category.DisplayOrder,
                IsActive = category.IsActive,
                ChildCategories = ChildrenOf(category.Id)
                    .Where(child => !_visited.Contains(child.Id))
                    .Select(Build)
                    .ToList()
            };
        }
    }
}
