using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkUpdateProductPrices;

public class BulkUpdateProductPricesCommandHandler : IRequestHandler<BulkUpdateProductPricesCommand, Result<BulkProductReport>>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public BulkUpdateProductPricesCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BulkProductReport>> Handle(BulkUpdateProductPricesCommand request, CancellationToken cancellationToken)
    {
        // The validator has refused a repeated id, so this cannot throw on a duplicate key.
        var prices = request.Items!.ToDictionary(i => i.ProductId, i => i.Price);

        var report = await BulkProductProcessor.ApplyAsync(
            request.Items!.Select(i => i.ProductId).ToList(),
            _productRepository,
            _unitOfWork,
            // UpdatePrice checks every rule before it assigns, so a refused price leaves the product as loaded.
            product => product.UpdatePrice(prices[product.Id]),
            cancellationToken);

        return Result<BulkProductReport>.Success(report);
    }
}
