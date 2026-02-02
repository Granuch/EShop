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
}