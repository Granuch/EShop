using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Products.Commands.DeleteProduct;

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IProductRepository _productRepository;
    private readonly ILogger<DeleteProductCommandHandler> _logger;

    public DeleteProductCommandHandler(IProductRepository productRepository, ILogger<DeleteProductCommandHandler> logger)
    {
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Deleting product with id {request.ProductId}");
        
        var product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        
        if(product is null)
            return Result.Failure(new Error("Not found", $"Product with id {request.ProductId} not found"));
        
        product.SoftDelete();
        
        await _productRepository.UpdateAsync(product, cancellationToken);
        
        _logger.LogInformation($"Successfully deleted product with id {request.ProductId}");
        return Result.Success();
    }
}