using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ClearProductDiscount;

public class ClearProductDiscountCommandHandler : IRequestHandler<ClearProductDiscountCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public ClearProductDiscountCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(ClearProductDiscountCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Idempotent in the domain: clearing an absent discount is a no-op, so a retried request
        // gets 204 rather than an error. The save and the invalidation still run — they are cheap,
        // and skipping them would mean the "already clear" path behaved differently from the
        // "just cleared" one for no observable benefit.
        product.ClearDiscountPrice();

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // products:category:{id} goes through ICacheInvalidationContext rather than the command's
        // CacheKeysToInvalidate because the command carries only ProductId — the CategoryId is only
        // known once the product is loaded. CacheInvalidationBehavior drains both, after
        // TransactionBehavior has committed.
        _cacheInvalidationContext.AddKey(ProductCacheKeys.Category(product.CategoryId));

        return Result.Success();
    }
}
