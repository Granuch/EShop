using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ReplaceProductAttributes;

public class ReplaceProductAttributesCommandHandler : IRequestHandler<ReplaceProductAttributesCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReplaceProductAttributesCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReplaceProductAttributesCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // The cap and the duplicate-name rule are enforced in both places on purpose: the validator
        // answers first with a field-level message, and the domain answers for any caller that does
        // not go through this command. Neither is redundant with the other.
        product.ReplaceAttributes(
            request.Attributes!.Select(a => (a.Name, a.Value)).ToList());

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
