using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AddProductImage;

public class AddProductImageCommandHandler : IRequestHandler<AddProductImageCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public AddProductImageCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result<Guid>> Handle(AddProductImageCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result<Guid>.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Domain guards (URL shape, duplicate URL, the 10-image cap) throw DomainException,
        // which GlobalExceptionHandlerMiddleware maps to 400 — no catch needed here.
        var imageId = product.AddImage(request.Url, request.AltText, request.DisplayOrder);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // products:category:{id} goes through ICacheInvalidationContext rather than the
        // command's CacheKeysToInvalidate because the command carries only ProductId — the
        // CategoryId is only known once the product is loaded. CacheInvalidationBehavior
        // drains both, after TransactionBehavior has committed.
        _cacheInvalidationContext.AddKey($"products:category:{product.CategoryId}");

        return Result<Guid>.Success(imageId);
    }
}
