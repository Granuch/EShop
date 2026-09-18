using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Domain.Interfaces;

/// <summary>
/// Repository interface for Category aggregate
/// </summary>
public interface ICategoryRepository
{
    Task<Category?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
    Task UpdateAsync(Category category, CancellationToken cancellationToken = default);
    /// <param name="includeInactive">
    /// A4 (Admin panel S5). True lifts the <c>c.IsActive</c> global query filter so deactivated
    /// categories and their deactivated children appear. <b>Admin-only</b>, decided at the endpoint
    /// from the caller's role, and part of <c>GetCategoriesQuery</c>'s cache key — without it in
    /// the key, one admin request poisons the shared entry for every anonymous caller.
    /// </param>
    Task<List<Category>> GetRootCategories(bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a live category already holds <paramref name="slug"/> under
    /// <paramref name="parentCategoryId"/> (null = among roots). Deleted categories do not count,
    /// matching the IsActive-filtered unique indexes.
    /// </summary>
    Task<bool> SlugExistsAsync(Guid? parentCategoryId, string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a category for tracking <b>including deactivated ones</b> (Admin panel S5), via
    /// <c>IgnoreQueryFilters()</c>.
    /// </summary>
    /// <remarks>
    /// Only the restore path may use this. <see cref="GetById"/> runs under the <c>c.IsActive</c>
    /// global filter and so answers null for exactly the categories restore exists to act on; every
    /// other write path must keep treating a deleted category as absent.
    /// </remarks>
    Task<Category?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The <b>persisted</b> ancestor chain of <paramref name="categoryId"/>, nearest parent first,
    /// read with a recursive CTE (Admin panel S5).
    /// </summary>
    /// <remarks>
    /// This exists so <c>Category.MoveTo</c>'s cycle check never touches the
    /// <c>ParentCategory</c> navigation. EF populates that only as far as a query happened to
    /// <c>Include</c>, so walking it answers "no cycle" for any chain deeper than was loaded —
    /// the Stage 8 bug that made a category's parent immutable in the first place. It deliberately
    /// ignores the <c>IsActive</c> filter: a deactivated category still occupies a position in the
    /// tree, and a cycle through one is just as much a cycle.
    /// </remarks>
    Task<List<Guid>> GetAncestorIdsAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live children of <paramref name="parentCategoryId"/> (null = the roots), for the batch
    /// reorder (Admin panel S5).
    /// </summary>
    Task<List<Category>> GetSiblingsAsync(Guid? parentCategoryId, CancellationToken cancellationToken = default);
}
