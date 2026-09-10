using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.PublishProduct;

public class PublishProductCommandHandler : IRequestHandler<PublishProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PublishProductCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PublishProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Idempotent: publishing an already-published product is a no-op rather than an error, so a
        // retried request does not 400. Product.Publish would throw for this case, and a
        // DomainException surfaces as 400 — correct for Discontinued, wrong for a duplicate call.
        if (product.Status == ProductStatus.Active)
            return Result.Success();

        // Anything else non-Draft (Discontinued) is a genuine rejection and Product.Publish's own
        // guard throws it as a DomainException -> 400.
        product.Publish();

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
