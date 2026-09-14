using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly CatalogDbContext _context;

    public CategoryRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<Category?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Categories
            .Include(c => c.ParentCategory)
            .Include(c => c.ChildCategories.OrderBy(child => child.DisplayOrder).ThenBy(child => child.Name))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        await _context.Categories.AddAsync(category, cancellationToken);
    }

    public Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        // No explicit Update() — entity is change-tracked.
        return Task.CompletedTask;
    }

    /// <summary>
    /// M11. Ordered by DisplayOrder, then Name, then Id. DisplayOrder alone was the only key, and
    /// every category had 0 (create could not set it), so the root order was whatever Postgres
    /// returned — and that order was then cached for ten minutes.
    /// </summary>
    public async Task<List<Category>> GetRootCategories(CancellationToken cancellationToken = default)
    {
        return await _context.Categories
            .Where(c => c.ParentCategoryId == null)
            .Include(c => c.ChildCategories.OrderBy(child => child.DisplayOrder).ThenBy(child => child.Name))
                .ThenInclude(c => c.ChildCategories.OrderBy(grandchild => grandchild.DisplayOrder).ThenBy(grandchild => grandchild.Name))
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Take(100)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<bool> SlugExistsAsync(Guid? parentCategoryId, string slug, CancellationToken cancellationToken = default)
        => _context.Categories
            .AsNoTracking()
            .AnyAsync(c => c.ParentCategoryId == parentCategoryId && c.Slug == slug, cancellationToken);
}
