using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidator cacheInvalidator,
        ILogger<UpdateProductCommandHandler> logger)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
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

        // Invalidate category product-list cache (not covered by ICacheInvalidatingCommand
        // because the command doesn't know the CategoryId at construction time)
        await _cacheInvalidator.InvalidateAsync($"products:category:{product.CategoryId}", cancellationToken);

        // DEBT-16. The products:list:* keys embed every filter/sort/page parameter, so
        // they cannot be named for exact-key invalidation. Bumping the family version
        // makes all of them unreachable in one operation instead of leaving list results
        // stale for the full 5-minute TTL.
        await _cacheInvalidator.InvalidateFamilyAsync(
            ProductCacheFamilies.ProductList, cancellationToken);

        return Result.Success();
    }
}