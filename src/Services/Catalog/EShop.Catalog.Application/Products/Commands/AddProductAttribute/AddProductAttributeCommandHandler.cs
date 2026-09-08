using EShop.BuildingBlocks.Application;
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
    private readonly ICacheInvalidator _cacheInvalidator;

    public AddProductAttributeCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidator cacheInvalidator)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
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

        // Invalidate category product-list cache (not covered by ICacheInvalidatingCommand
        // because the command doesn't know the CategoryId at construction time).
        await _cacheInvalidator.InvalidateAsync($"products:category:{product.CategoryId}", cancellationToken);

        // DEBT-16. The products:list:* keys embed every filter/sort/page parameter, so
        // they cannot be named for exact-key invalidation. Bumping the family version
        // makes all of them unreachable in one operation instead of leaving list results
        // stale for the full 5-minute TTL.
        await _cacheInvalidator.InvalidateFamilyAsync(
            ProductCacheFamilies.ProductList, cancellationToken);

        return Result<Guid>.Success(attributeId);
    }
}
