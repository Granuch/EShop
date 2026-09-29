using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProductImage;

public class UpdateProductImageCommandHandler : IRequestHandler<UpdateProductImageCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductImageCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateProductImageCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Checked here rather than letting Product.UpdateImage throw: a DomainException would
        // surface as 400 via ProblemDetailsExceptionMiddleware, and a missing image must be 404.
        // Same shape as RemoveProductImageCommandHandler.
        if (product.Images.All(i => i.Id != request.ImageId))
            return Result.Failure(new Error("ProductImage.NotFound", $"Image with ID '{request.ImageId}' was not found on product '{request.ProductId}'."));

        // The duplicate-URL guard (which excludes this image, so re-sending an unchanged URL is
        // allowed) and the URL shape rules both throw DomainException → 400. No catch needed.
        product.UpdateImage(request.ImageId, request.Url, request.AltText);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
