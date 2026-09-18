using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.RemoveProductAttribute;

public class RemoveProductAttributeCommandHandler : IRequestHandler<RemoveProductAttributeCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveProductAttributeCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveProductAttributeCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Checked here rather than letting Product.RemoveAttribute throw: a DomainException would
        // surface as 400, and a missing attribute must be 404. Same shape as RemoveProductImage.
        if (product.Attributes.All(a => a.Id != request.AttributeId))
            return Result.Failure(new Error("ProductAttribute.NotFound", $"Attribute with ID '{request.AttributeId}' was not found on product '{request.ProductId}'."));

        product.RemoveAttribute(request.AttributeId);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
