using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.RemoveProductImage;

public class RemoveProductImageCommandHandler : IRequestHandler<RemoveProductImageCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidator _cacheInvalidator;

    public RemoveProductImageCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidator cacheInvalidator)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task<Result> Handle(RemoveProductImageCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Checked here rather than letting Product.RemoveImage throw: a DomainException would
        // surface as 400 via GlobalExceptionHandlerMiddleware, and a missing image must be 404.
        if (product.Images.All(i => i.Id != request.ImageId))
            return Result.Failure(new Error("ProductImage.NotFound", $"Image with ID '{request.ImageId}' was not found on product '{request.ProductId}'."));

        product.RemoveImage(request.ImageId);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Invalidate category product-list cache (not covered by ICacheInvalidatingCommand
        // because the command doesn't know the CategoryId at construction time).
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
