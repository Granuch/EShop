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
            .Include(c => c.Products)
            .Include(c => c.ChildCategories)
            .ThenInclude(cc => cc.Products) // products of first-level child categories
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
    
    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        if(category == null)
            throw new ArgumentNullException(nameof(category));
        
        await _context.Categories.AddAsync(category, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
    
    public async Task DeleteAsync(Category category, CancellationToken cancellationToken = default)
    {
        _context.Categories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        _context.Categories.Update(category);
        await _context.SaveChangesAsync(cancellationToken);
    }
    
    public async Task<List<Category>> GetRootCategories(CancellationToken ct = default)
    {
        return await _context.Categories
            .Where(c => c.ParentCategoryId == null)
            .Include(c => c.ChildCategories)
            .ThenInclude(c => c.ChildCategories) // load next level (can repeat)
            .Include(c => c.Products)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}