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
    Task<List<Category>> GetRootCategories(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a live category already holds <paramref name="slug"/> under
    /// <paramref name="parentCategoryId"/> (null = among roots). Deleted categories do not count,
    /// matching the IsActive-filtered unique indexes.
    /// </summary>
    Task<bool> SlugExistsAsync(Guid? parentCategoryId, string slug, CancellationToken cancellationToken = default);
}
