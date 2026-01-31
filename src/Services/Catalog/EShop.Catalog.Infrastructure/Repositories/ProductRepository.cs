using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EShop.Catalog.Infrastructure.Repositories;

//TODO SearchAsync

public class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    
    public ProductRepository(CatalogDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"product_{id}";

        if (_cache.TryGetValue(cacheKey, out Product? cachedProduct))
            return cachedProduct;
        
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product != null)
        {
            _cache.Set(cacheKey, product, _cacheDuration);
        }
        
        return product;
    }

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"product_{sku}";
        
        if (_cache.TryGetValue(cacheKey, out Product? cachedProduct))
            return cachedProduct;
        
        var product = await _context.Products.FirstOrDefaultAsync(s => s.Sku == sku, cancellationToken);
        
        if (product != null)
        {
            _cache.Set(cacheKey, product, _cacheDuration);
        }
        
        return product;
    }

    public async Task<IEnumerable<Product?>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products.Where(c => c.CategoryId == categoryId)
            .ToListAsync<Product>(cancellationToken);
        
        return products;
    }
    

    public async Task<PagedResult<Product>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (pageNumber <= 0) pageNumber = 1;
        if (pageSize <= 0) pageSize = 10;

        var query = _context.Products.AsQueryable();

        // Total count of products
        var totalCount = await query.CountAsync(cancellationToken);

        // Get the current page
        var items = await query
            .OrderBy(p => p.Name) // always order for consistent paging
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Return paged result
        return new PagedResult<Product>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        if(product == null)
            throw new ArgumentNullException(nameof(product));
        
        await _context.Products.AddAsync(product);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Update(product);
        await _context.SaveChangesAsync(cancellationToken);
        
        _cache.Remove($"product_{product.Id}");
        _cache.Remove($"product_{product.Sku}");
    }

    public async Task DeleteAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Remove(product);
        await _context.SaveChangesAsync(cancellationToken);
        
        _cache.Remove($"product_{product.Id}");
        _cache.Remove($"product_{product.Sku}");
    }

    public Task<IEnumerable<Product>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IQueryable<Product> Query()
    {
        return _context.Products.AsNoTracking();
    }
}