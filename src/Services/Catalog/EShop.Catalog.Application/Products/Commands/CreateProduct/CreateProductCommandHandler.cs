using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Telemetry;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Handler for creating a product
/// </summary>
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidator _cacheInvalidator;

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidator cacheInvalidator)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        using var activity = CatalogActivitySource.Source.StartActivity("Catalog.CreateProduct");
        activity?.SetTag("product.sku", request.Sku);
        activity?.SetTag("product.category_id", request.CategoryId.ToString());

        // Read-then-write, so it cannot be the whole answer: two concurrent creates both pass this
        // and only the partial unique index IX_Products_Sku stops the second. That loss surfaces as
        // a 409 Product.SkuConflict via CatalogProblemDetailsExtensions, not as a 400 like this
        // branch — the distinction is real, since a race is retryable and a plain duplicate is not.
        if (await _productRepository.SkuExistsAsync(request.Sku, cancellationToken))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "sku_conflict");
            return Result<Guid>.Failure(new Error("Product.SkuConflict", $"Product with SKU '{request.Sku}' already exists."));
        }

        var category = await _categoryRepository.GetById(request.CategoryId, cancellationToken);
        if (category == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "category_not_found");
            return Result<Guid>.Failure(new Error("Category.NotFound", $"Category with ID '{request.CategoryId}' was not found."));
        }

        var product = Product.Create(request.Name, request.Sku, request.Price, request.StockQuantity, request.CategoryId, request.Description);

        // Images and attributes are applied before the product is persisted, so a rejected
        // image leaves nothing behind — no partial product. The command is also
        // ITransactionalCommand, which covers anything that fails after SaveChangesAsync.
        foreach (var image in request.Images ?? [])
        {
            product.AddImage(image.Url, image.AltText, image.DisplayOrder);
        }

        foreach (var attribute in request.Attributes ?? [])
        {
            product.AddAttribute(attribute.Name, attribute.Value);
        }

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // This handler invalidated nothing before DEBT-16, which was the most visible instance of
        // the stale-list problem: a newly created product did not appear in any cached list for up
        // to five minutes, so the POST looked like it had silently failed.
        await _cacheInvalidator.InvalidateAsync(
            $"products:category:{product.CategoryId}", cancellationToken);
        await _cacheInvalidator.InvalidateFamilyAsync(
            ProductCacheFamilies.ProductList, cancellationToken);

        activity?.SetTag("product.id", product.Id.ToString());

        return Result<Guid>.Success(product.Id);
    }
}
