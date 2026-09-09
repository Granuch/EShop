using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AddProductAttribute;

public class AddProductAttributeCommandHandler : IRequestHandler<AddProductAttributeCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public AddProductAttributeCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result<Guid>> Handle(AddProductAttributeCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result<Guid>.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Domain guards throw DomainException, which GlobalExceptionHandlerMiddleware maps to
        // 400 — no catch needed here. That covers empty/over-length name and value, the
        // 50-attribute cap, and duplicate names (compared trimmed and case-insensitively).
        // The cap and dedupe live in Product.AddAttribute rather than in this command's
        // validator so they hold on both entry paths: CreateProductCommandValidator checks the
        // inline collection it can see, but only the aggregate knows what is already persisted.
        var attributeId = product.AddAttribute(request.Name, request.Value);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // products:category:{id} goes through ICacheInvalidationContext rather than the
        // command's CacheKeysToInvalidate because the command carries only ProductId — the
        // CategoryId is only known once the product is loaded. CacheInvalidationBehavior
        // drains both, after TransactionBehavior has committed.
        _cacheInvalidationContext.AddKey($"products:category:{product.CategoryId}");

        return Result<Guid>.Success(attributeId);
    }
}
