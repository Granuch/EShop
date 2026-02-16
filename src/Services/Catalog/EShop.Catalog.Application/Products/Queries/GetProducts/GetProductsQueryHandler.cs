using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// Handler for getting products with filtering and pagination.
/// Uses EF Core Select projection to avoid over-fetching.
/// Caching is handled by CachingBehavior via ICacheableQuery.
/// </summary>
public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, Result<PagedResult<ProductDto>>>
{
    private readonly IProductRepository _productRepository;

    public GetProductsQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var query = _productRepository.Query();

        // Filtering
        if (request.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = $"%{request.SearchTerm.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, term) ||
                EF.Functions.ILike(p.Sku, term));
        }

        if (request.MinPrice.HasValue)
            query = query.Where(p => p.Price >= request.MinPrice.Value);

        if (request.MaxPrice.HasValue)
            query = query.Where(p => p.Price <= request.MaxPrice.Value);

        // Sorting
        query = request.SortBy switch
        {
            ProductSortBy.Price => request.IsDescending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            ProductSortBy.CreatedAt => request.IsDescending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => request.IsDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
        };

        var totalCount = await query.CountAsync(cancellationToken);

        // Select projection — only fetch columns needed for DTO (avoids over-fetching)
        var dtos = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
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

        var pagedResult = PagedResult<ProductDto>.Create(dtos, request.PageNumber, request.PageSize, totalCount);

        return Result<PagedResult<ProductDto>>.Success(pagedResult);
    }
}
