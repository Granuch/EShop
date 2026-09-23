using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _context;

    public ProductRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Attributes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .IgnoreQueryFilters()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Attributes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Attributes)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<bool> SkuExistsAsync(string sku, Guid? excludingProductId = null, CancellationToken cancellationToken = default)
    {
        // AnyAsync, not FirstOrDefaultAsync: the only caller asks an existence question, and the
        // old form materialised and tracked a whole Product to answer it. AsNoTracking keeps the
        // result out of the change tracker even if EF's translation ever stops eliding it.
        //
        // The comparison is ordinal, matching both the column's collation and the unique index, so
        // "ABC" and "abc" are two SKUs here and in the database alike.
        return await _context.Products
            .AsNoTracking()
            .AnyAsync(
                p => p.Sku == sku && (excludingProductId == null || p.Id != excludingProductId),
                cancellationToken);
    }

    public async Task<bool> AnyInCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .AsNoTracking()
            .AnyAsync(p => p.CategoryId == categoryId, cancellationToken);
    }

    public async Task<List<Product>> GetByIdsWithoutChildrenAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        // Tracked, because every caller mutates what it loads; no Include, because none of them touches a child — see
        // the interface. Contains over a collection becomes `"Id" = ANY(@ids)` on Npgsql: one parameter, whatever the
        // count, so the bulk cap bounds the row count and not the statement's shape.
        return await _context.Products
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetTakenSkusAsync(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken = default)
    {
        var taken = await _context.Products
            .AsNoTracking()
            .Where(p => skus.Contains(p.Sku))
            .Select(p => p.Sku)
            .ToListAsync(cancellationToken);

        // Ordinal, matching SkuExistsAsync, the column's collation and IX_Products_Sku.
        return new HashSet<string>(taken, StringComparer.Ordinal);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _context.Products.AddAsync(product, cancellationToken);
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        // No explicit Update() call needed — entity was loaded with tracking,
        // so EF Core detects property changes automatically on SaveChanges.
        // Calling Update() would force a full-row update and interfere with concurrency tokens.
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Remove(product);
        return Task.CompletedTask;
    }
}