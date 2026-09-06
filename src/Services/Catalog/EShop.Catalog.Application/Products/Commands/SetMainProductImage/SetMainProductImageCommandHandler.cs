using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.SetMainProductImage;

public class SetMainProductImageCommandHandler : IRequestHandler<SetMainProductImageCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidator _cacheInvalidator;

    public SetMainProductImageCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidator cacheInvalidator)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task<Result> Handle(SetMainProductImageCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Checked here rather than letting Product.SetMainImage throw: a DomainException would
        // surface as 400 via GlobalExceptionHandlerMiddleware, and a missing image must be 404.
        if (product.Images.All(i => i.Id != request.ImageId))
            return Result.Failure(new Error("ProductImage.NotFound", $"Image with ID '{request.ImageId}' was not found on product '{request.ProductId}'."));

        // Already the main image: nothing to write, and demoting/re-promoting the same row
        // would emit two pointless UPDATEs.
        if (product.Images.Any(i => i.Id == request.ImageId && i.IsMain))
            return Result.Success();

        // The main flag is moved in two persisted steps, demotion first. "At most one main"
        // is enforced by a non-deferrable partial unique index, and EF Core does not
        // guarantee it orders the UNSET UPDATE before the SET one within a single
        // SaveChanges — when the SET went first, Postgres saw two IsMain rows and failed the
        // batch with 23505, surfacing as an intermittent 409 (~1 in 3 calls). Both saves run
        // inside the transaction opened by TransactionBehavior for this ITransactionalCommand,
        // so the intermediate zero-main state is never visible outside it and rolls back
        // together on failure.
        if (product.ClearMainImage())
        {
            await _productRepository.UpdateAsync(product, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        product.SetMainImage(request.ImageId);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Invalidate category product-list cache (not covered by ICacheInvalidatingCommand
        // because the command doesn't know the CategoryId at construction time).
        // The products:list:* family cannot be invalidated at all — ICacheInvalidatingCommand
        // supports exact keys only and those keys embed every filter/sort/page parameter, so
        // list results reflect the new main image only after the 5-minute TTL expires.
        await _cacheInvalidator.InvalidateAsync($"products:category:{product.CategoryId}", cancellationToken);

        return Result.Success();
    }
}
