using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDistributedCache _cache;
    private readonly CachingBehaviorOptions _cacheOptions;
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        IDistributedCache cache,
        ILogger<UpdateProductCommandHandler> logger,
        IOptions<CachingBehaviorOptions>? cacheOptions = null)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
        _cacheOptions = cacheOptions?.Value ?? new CachingBehaviorOptions();
    }

    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        product.UpdatePrice(request.Price);
        product.UpdateStock(request.StockQuantity);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Invalidate category product-list cache (not covered by ICacheInvalidatingCommand)
        try
        {
            var categoryKey = $"{_cacheOptions.KeyPrefix}{_cacheOptions.Version}:products:category:{product.CategoryId}";
            await _cache.RemoveAsync(categoryKey, cancellationToken);
            _logger.LogDebug("Invalidated category product-list cache {CacheKey} after product update", categoryKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate category cache after product update for {ProductId}", request.ProductId);
        }

        return Result.Success();
    }
}