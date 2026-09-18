using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProductAttribute;

public class UpdateProductAttributeCommandHandler : IRequestHandler<UpdateProductAttributeCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductAttributeCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateProductAttributeCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // Checked here rather than letting Product.UpdateAttribute throw: a DomainException would
        // surface as 400, and a missing attribute must be 404. Same shape as the image handlers.
        if (product.Attributes.All(a => a.Id != request.AttributeId))
            return Result.Failure(new Error("ProductAttribute.NotFound", $"Attribute with ID '{request.AttributeId}' was not found on product '{request.ProductId}'."));

        // A name already used by a SIBLING attribute throws DomainException → 400. A name that
        // arrives concurrently from another request instead reaches M1's unique index and is mapped
        // to 409 Product.AttributeConflict by AddProductAttributeConflict() — the two answer
        // different questions, exactly as with SKU.
        product.UpdateAttribute(request.AttributeId, request.Name, request.Value);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
