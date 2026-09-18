using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Every check runs BEFORE the first mutation. TransactionBehavior commits on any
        // non-exception return, a failure Result included, so a handler that mutated first and then
        // returned a failure would persist the half it had already applied.
        var newSku = request.Sku?.Trim();
        if (newSku is not null && !string.Equals(newSku, product.Sku, StringComparison.Ordinal))
        {
            // Read-then-write, so it is not the whole answer: two concurrent updates both pass
            // here and only IX_Products_Sku stops the second, surfacing as 409 Product.SkuConflict
            // through AddProductSkuConflict().
            //
            // Two lines protect "re-sending your own SKU is allowed" — the `!= product.Sku` guard
            // above and the exclusion below — and breaking either one alone leaves
            // Update_ResendingItsOwnSku_IsAllowed green (verified; only removing both together
            // turned it red). The guard is the one that fires today, and it also saves a pointless
            // query; the exclusion is kept because it is what keeps this correct if the guard is
            // ever relaxed, for instance to compare SKUs case-insensitively. Do not read the
            // exclusion as the reason the admin form works.
            if (await _productRepository.SkuExistsAsync(newSku, request.ProductId, cancellationToken))
                return Result.Failure(new Error("Product.SkuConflict", $"Product with SKU '{newSku}' already exists."));
        }

        if (request.CategoryId is { } categoryId && categoryId != product.CategoryId)
        {
            var category = await _categoryRepository.GetById(categoryId, cancellationToken);
            if (category == null)
                return Result.Failure(new Error("Category.NotFound", $"Category with ID '{categoryId}' was not found."));
        }

        // Name and SKU fall back to what the product already holds, so "omitted means leave it"
        // survives into the domain, which takes both as required.
        product.UpdateDetails(
            request.Name ?? product.Name,
            request.Description,
            newSku ?? product.Sku);

        if (request.CategoryId is { } newCategoryId)
            product.ChangeCategory(newCategoryId);

        // Price before stock, and both after the descriptive fields: UpdatePrice refuses a price at
        // or below an active discount and throws, which the middleware maps to 400. Nothing is
        // persisted until SaveChangesAsync, so an early throw leaves the row untouched.
        product.UpdatePrice(request.Price);
        product.UpdateStock(request.StockQuantity);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
