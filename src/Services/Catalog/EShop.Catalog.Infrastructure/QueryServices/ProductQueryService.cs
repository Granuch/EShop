using System.Linq.Expressions;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetNewestProducts;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Infrastructure.QueryServices;

/// <summary>
/// Infrastructure implementation of IProductQueryService.
/// Contains all provider-specific query composition (EF.Functions.ILike, row-value comparison, etc.).
/// </summary>
public class ProductQueryService : IProductQueryService
{
    private readonly CatalogDbContext _context;

    public ProductQueryService(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<(List<ProductDto> Items, int TotalCount)> GetFilteredProductsAsync(
        ProductListFilter filter,
        ProductSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.Products.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        // Id is the tiebreaker on every sort. Name, Price and CreatedAt all repeat, and without a
        // unique final key Postgres may order tied rows differently on each execution — so OFFSET
        // paging could repeat one row on two pages and never show another.
        query = sortBy switch
        {
            ProductSortBy.Price => isDescending
                ? query.OrderByDescending(p => p.Price).ThenBy(p => p.Id)
                : query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            ProductSortBy.CreatedAt => isDescending
                ? query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
                : query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id),
            _ => isDescending
                ? query.OrderByDescending(p => p.Name).ThenBy(p => p.Id)
                : query.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

        var dtos = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return (dtos, totalCount);
    }

    public async Task<List<ProductDto>> GetNewestProductsAsync(
        ProductListFilter filter,
        ProductCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.Products.AsNoTracking(), filter);

        if (after is { } cursor)
        {
            // H4. A row-value comparison, `("CreatedAt", "Id") < (@createdAt, @id)`, rather than the
            // equivalent `CreatedAt < c OR (CreatedAt = c AND Id < id)`: Postgres serves the tuple
            // form as a single range scan on IX_Products_CreatedAt_Id, which is what makes a deep
            // page cost the same as the first. The old predicate was `CreatedAt < c` alone, which
            // skipped every product sharing the boundary timestamp.
            //
            // Both sides of the comparison — and the ORDER BY below — run in SQL, so Postgres's own
            // uuid ordering is used throughout. It differs from System.Guid's CompareTo, which is
            // why no part of this may move to the client.
            var createdAt = cursor.CreatedAt;
            var id = cursor.Id;
            query = query.Where(p => EF.Functions.LessThan(
                ValueTuple.Create(p.CreatedAt, p.Id),
                ValueTuple.Create(createdAt, id)));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(take)
            .Select(ToDto)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The filters every list read applies. Status first, and before any count: TotalCount must
    /// describe what the caller can actually reach, or it reports pages that come back empty.
    /// </summary>
    private static IQueryable<Product> ApplyFilter(IQueryable<Product> query, ProductListFilter filter)
    {
        // D1 / H5a. Public callers see published products only.
        if (!filter.IncludeUnpublished)
            query = query.Where(p => p.Status == ProductStatus.Active);

        if (filter.CategoryId.HasValue)
        {
            var categoryId = filter.CategoryId.Value;
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            // Escape LIKE special characters to prevent wildcard injection
            var escaped = filter.SearchTerm.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
            var term = $"%{escaped}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, term, "\\") ||
                EF.Functions.ILike(p.Sku, term, "\\"));
        }

        if (filter.MinPrice.HasValue)
        {
            var minPrice = filter.MinPrice.Value;
            query = query.Where(p => p.Price >= minPrice);
        }

        if (filter.MaxPrice.HasValue)
        {
            var maxPrice = filter.MaxPrice.Value;
            query = query.Where(p => p.Price <= maxPrice);
        }

        return query;
    }

    /// <summary>
    /// The one list projection. There used to be two copies of it (list and by-category), and a
    /// field added to ProductDto had to be added to both.
    ///
    /// MainImageUrl is a correlated subquery, not a join: EF translates it to a scalar subselect per
    /// row, bounded by page size (capped at 100 by every list validator). It relies on the composite
    /// IX_ProductImages_ProductId (ProductId, IsMain, DisplayOrder) INCLUDE (Url) index for an
    /// index-only scan — see catalog-images-variant-a-plan.md §2.
    /// </summary>
    private static readonly Expression<Func<Product, ProductDto>> ToDto = p => new ProductDto
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
            .OrderByDescending(i => i.IsMain)
            .ThenBy(i => i.DisplayOrder)
            .ThenBy(i => i.CreatedAt)
            .Select(i => i.Url)
            .FirstOrDefault(),
        CreatedAt = p.CreatedAt
    };
}
