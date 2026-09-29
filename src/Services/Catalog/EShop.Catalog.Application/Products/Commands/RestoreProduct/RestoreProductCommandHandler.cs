using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.RestoreProduct;

public class RestoreProductCommandHandler : IRequestHandler<RestoreProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RestoreProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RestoreProductCommand request, CancellationToken cancellationToken)
    {
        // GetByIdAsync would answer null here whatever the state of the database: the !IsDeleted
        // global query filter hides exactly the products this command exists to act on.
        var product = await _productRepository.GetByIdIncludingDeletedAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        if (!product.IsDeleted)
            return Result.Failure(new Error("Product.NotDeleted", $"Product with ID '{request.ProductId}' is not deleted."));

        // F-43 (frontend-contracts R5). A product restored into a deleted category became
        // unreachable: every ProductRepository.GetById* includes the required Category navigation,
        // and the category's IsActive filter turns that include into an inner join that drops the
        // product — so the restore answered 204, the product showed in admin lists, and its detail
        // read and every write answered 404. Refused instead, naming the one fix that exists: a
        // deleted product cannot be moved to another category (PUT answers 404 for it).
        // GetExistingIdsAsync runs under the same IsActive filter as those reads.
        var liveCategories = await _categoryRepository.GetExistingIdsAsync([product.CategoryId], cancellationToken);
        if (!liveCategories.Contains(product.CategoryId))
        {
            return Result.Failure(new Error("Product.CategoryNotActive",
                $"Product '{request.ProductId}' belongs to category '{product.CategoryId}', which is deleted. Restore the category first (POST /api/v1/categories/{product.CategoryId}/restore), then restore this product."));
        }

        // A3. IX_Products_Sku is unique filtered NOT "IsDeleted", so this product's SKU became free
        // the moment it was deleted and another product may have taken it. Restoring re-enters the
        // filtered index, and without this check that is a raw 23505 from the database rather than
        // an answer the admin can act on. SkuExistsAsync runs under the same global filter as the
        // index's own predicate, so the two agree about what "taken" means.
        //
        // Every check runs BEFORE Restore() mutates anything: TransactionBehavior commits on any
        // non-exception return, so a failure Result after a mutation would persist it.
        if (await _productRepository.SkuExistsAsync(product.Sku, cancellationToken: cancellationToken))
        {
            return Result.Failure(new Error("Product.SkuConflict",
                $"SKU '{product.Sku}' is already used by another product. Change that product's SKU before restoring this one."));
        }

        product.Restore();

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
