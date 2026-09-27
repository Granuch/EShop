using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ReorderProductImages;

public class ReorderProductImagesCommandHandler : IRequestHandler<ReorderProductImagesCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReorderProductImagesCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReorderProductImagesCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // An id that is not this product's is a 404, so it is pre-checked here rather than left to
        // the domain's DomainException (which would be a 400). The count and duplicate rules stay
        // in the domain: those describe a malformed request, not a missing resource, and 400 is the
        // right answer for them.
        //
        // Projected to Guid? on purpose: a plain FirstOrDefault returns Guid.Empty for "no unknown
        // id", which is indistinguishable from a caller that supplied Guid.Empty. The validator
        // rejects that today, so the plain form would work — and would silently start reporting
        // "all ids known" the day that rule moved or was relaxed.
        var unknownImageId = request.ImageIds!
            .Where(id => product.Images.All(i => i.Id != id))
            .Select(id => (Guid?)id)
            .FirstOrDefault();

        if (unknownImageId is not null)
            return Result.Failure(new Error("ProductImage.NotFound", $"Image with ID '{unknownImageId}' was not found on product '{request.ProductId}'."));

        // Count mismatch and duplicate ids throw DomainException → 400.
        product.ReorderImages(request.ImageIds!);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
