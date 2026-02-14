using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Domain.Interfaces;

public interface ICategoryRepository
{
    Task<Category?> GetById(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
    Task UpdateAsync(Category category, CancellationToken cancellationToken = default);
    Task DeleteAsync(Category category, CancellationToken cancellationToken = default);
    Task<List<Category>> GetRootCategories(CancellationToken ct = default);
}