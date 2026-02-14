using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ILogger<UpdateProductCommandHandler> _logger;

    public UpdateProductCommandHandler(IProductRepository productRepository, ILogger<UpdateProductCommandHandler> logger)
    {
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Updating product with id {request.ProductId}");

        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);

        if (product == null)
            return Result.Failure(new Error("Product not found",  $"Product {request.ProductId} not found"));
        
        product.UpdatePrice(request.Price);
        product.UpdateStock(request.StockQuantity);

        await _productRepository.UpdateAsync(product, cancellationToken);
        
        _logger.LogInformation($"Product with id {request.ProductId} updated");
        return Result.Success();
    }
}