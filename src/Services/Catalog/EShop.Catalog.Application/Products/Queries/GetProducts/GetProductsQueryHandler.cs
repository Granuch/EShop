using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// Handler for getting products with filtering and pagination
/// </summary>
public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, Result<PagedResult<ProductDto>>>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GetProductsQueryHandler> _logger;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public GetProductsQueryHandler(IProductRepository productRepository,  IMapper mapper, IMemoryCache cache, ILogger<GetProductsQueryHandler> logger)
    {
        _productRepository = productRepository;
        _mapper = mapper;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<PagedResult<ProductDto>>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        string cacheKey = $"products:{request.CategoryId}:{request.PageNumber}:{request.PageSize}:{request.SearchTerm}:{request.MinPrice}:{request.MaxPrice}:{request.SortBy}:{request.IsDescending}";

        if (_cache.TryGetValue(cacheKey, out PagedResult<ProductDto> cachedResult))
        {
            _logger.LogInformation("Returning products from cache with key {CacheKey}", cacheKey);
            return Result<PagedResult<ProductDto>>.Success(cachedResult);
        }

        var query = _productRepository.Query(); // <-- use the new Query() method

        // Filtering
        if (request.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            string term = request.SearchTerm.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Sku.ToLower().Contains(term));
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

        // Total count
        int totalCount = await query.CountAsync(cancellationToken);

        // Pagination
        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // Map to DTO
        var dtos = _mapper.Map<List<ProductDto>>(items);

        var pagedResult = new PagedResult<ProductDto>
        {
            Items = dtos,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };

        // Cache
        _cache.Set(cacheKey, pagedResult, _cacheDuration);
        _logger.LogInformation("Cached products with key {CacheKey} for {Duration} minutes", cacheKey, _cacheDuration.TotalMinutes);

        return Result<PagedResult<ProductDto>>.Success(pagedResult);
    }
}
