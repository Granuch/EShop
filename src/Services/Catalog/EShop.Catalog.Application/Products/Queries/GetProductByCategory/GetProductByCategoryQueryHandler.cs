using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public sealed class GetProductByCategoryQueryHandler : IRequestHandler<GetProductByCategoryQuery, Result<List<ProductDto>>>
{
    private readonly IMapper _mapper;
    private readonly ILogger<GetProductByCategoryQueryHandler> _logger;
    private readonly IProductRepository _productRepository;

    public GetProductByCategoryQueryHandler(IMapper mapper, ILogger<GetProductByCategoryQueryHandler> logger, IProductRepository productRepository)
    {
        _mapper = mapper;
        _logger = logger;
        _productRepository = productRepository;
    }

    public async Task<Result<List<ProductDto>>> Handle(GetProductByCategoryQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching products by Category with Id {CategoryId}", request.CategoryId);
        var products = await _productRepository.GetByCategoryAsync(request.CategoryId, cancellationToken);
        
        if (!products.Any())
            return Result<List<ProductDto>>.Failure(new Error("Not Found", $"Product with this category id: {request.CategoryId} not found"));
        
        var dtos = _mapper.Map<List<ProductDto>>(products);
        _logger.LogInformation("Fetched {Count} products for Category {CategoryId}", dtos.Count, request.CategoryId);
        return Result<List<ProductDto>>.Success(dtos);
    }
}