using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Infrastructure.QueryServices;

/// <summary>
/// Infrastructure implementation of IProductQueryService.
/// Contains all provider-specific query composition (EF.Functions.ILike, etc.).
/// </summary>
public class ProductQueryService : IProductQueryService
{
    private readonly CatalogDbContext _context;

    public ProductQueryService(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<(List<ProductDto> Items, int TotalCount)> GetFilteredProductsAsync(
        Guid? categoryId,
        string? searchTerm,
        decimal? minPrice,
        decimal? maxPrice,
        ProductSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Products.AsNoTracking();

        // Filtering
        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            // Escape LIKE special characters to prevent wildcard injection
            var escaped = searchTerm.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
            var term = $"%{escaped}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, term, "\\") ||
                EF.Functions.ILike(p.Sku, term, "\\"));
        }

        if (minPrice.HasValue)
            query = query.Where(p => p.Price >= minPrice.Value);

        if (maxPrice.HasValue)
            query = query.Where(p => p.Price <= maxPrice.Value);

        // Sorting
        query = sortBy switch
        {
            ProductSortBy.Price => isDescending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            ProductSortBy.CreatedAt => isDescending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => isDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
        };

        var totalCount = await query.CountAsync(cancellationToken);

        // Select projection — only fetch columns needed for DTO (avoids over-fetching)
        var dtos = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Sku = p.Sku,
                Price = p.Price,
                DiscountPrice = p.DiscountPrice,
                StockQuantity = p.StockQuantity,
                Status = p.Status,
                CategoryId = p.CategoryId,
                MainImageUrl = p.Images
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault(),
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (dtos, totalCount);
    }

    public async Task<List<ProductDto>> GetProductsByCategoryAsync(
        Guid categoryId,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .AsNoTracking()
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Name)
            .Take(maxResults)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Sku = p.Sku,
                Price = p.Price,
                DiscountPrice = p.DiscountPrice,
                StockQuantity = p.StockQuantity,
                Status = p.Status,
                CategoryId = p.CategoryId,
                MainImageUrl = p.Images
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault(),
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
