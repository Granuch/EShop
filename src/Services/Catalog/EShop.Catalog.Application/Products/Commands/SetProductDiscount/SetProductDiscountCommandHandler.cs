using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.SetProductDiscount;

public class SetProductDiscountCommandHandler : IRequestHandler<SetProductDiscountCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public SetProductDiscountCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(SetProductDiscountCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // "Discount must be below the list price" cannot live in the validator — it needs the
        // loaded product. Product.SetDiscountPrice throws a DomainException, which
        // ProblemDetailsExceptionMiddleware maps to 400 with its own message, so the client is told
        // which invariant it broke rather than getting a bare "Validation.Failed".
        product.SetDiscountPrice(request.DiscountPrice);

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
