using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.SetProductDiscount;

public class SetProductDiscountCommandHandler : IRequestHandler<SetProductDiscountCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetProductDiscountCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetProductDiscountCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // "Discount must be below the list price" cannot live in the validator — it needs the
        // loaded product. Product.SetDiscountPrice throws a DomainException, which
        // ProblemDetailsExceptionMiddleware maps to 400 with its own message, so the client is told
        // which invariant it broke rather than getting a bare "Validation.Failed".
        product.SetDiscountPrice(request.DiscountPrice);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
