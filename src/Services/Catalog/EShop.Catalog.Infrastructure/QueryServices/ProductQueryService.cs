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
        DateTime? cursor = null,
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

        // Cursor-based pagination: when cursor is provided and sorting by CreatedAt DESC,
        // use keyset pagination for constant-time performance regardless of page depth.
        var useCursorPagination = cursor.HasValue && sortBy == ProductSortBy.CreatedAt && isDescending;

        if (useCursorPagination)
        {
            query = query.Where(p => p.CreatedAt < cursor.Value);
        }

        var totalCount = useCursorPagination
            ? 0
            : await query.CountAsync(cancellationToken);

        // Sorting
        query = sortBy switch
        {
            ProductSortBy.Price => isDescending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            ProductSortBy.CreatedAt => isDescending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => isDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
        };

        // Select projection — only fetch columns needed for DTO (avoids over-fetching).
        // Images are NOT included in the listing to eliminate the LEFT JOIN overhead
        // on the ProductImages table (3.2M unnecessary index scans under load).
        var dtosQuery = query
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
                MainImageUrl = null,
                CreatedAt = p.CreatedAt
            });

        // Use cursor-based Take when cursor is provided, otherwise OFFSET pagination
        List<ProductDto> dtos;
        if (useCursorPagination)
        {
            dtos = await dtosQuery
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }
        else
        {
            dtos = await dtosQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }

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
                MainImageUrl = null,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }
}
