using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UnpublishProduct;

public class UnpublishProductCommandHandler : IRequestHandler<UnpublishProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public UnpublishProductCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(UnpublishProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Idempotent, for the same reason as PublishProductCommandHandler: a retried unpublish on a
        // product that is already Draft must not 400.
        if (product.Status == ProductStatus.Draft)
            return Result.Success();

        product.Unpublish();

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _cacheInvalidationContext.AddKey($"products:category:{product.CategoryId}");

        return Result.Success();
    }
}
