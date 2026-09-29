using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AdjustProductStock;

public class AdjustProductStockCommandHandler : IRequestHandler<AdjustProductStockCommand, Result<int>>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AdjustProductStockCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(AdjustProductStockCommand request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result<int>.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));

        // The validator has already rejected "neither" and "both", so this is a genuine either/or.
        // Domain guards (negative result, zero delta, deleted product) throw DomainException → 400.
        if (request.Delta.HasValue)
        {
            product.AdjustStock(request.Delta.Value);
        }
        else
        {
            product.UpdateStock(request.Absolute!.Value);
        }

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<int>.Success(product.StockQuantity);
    }
}
