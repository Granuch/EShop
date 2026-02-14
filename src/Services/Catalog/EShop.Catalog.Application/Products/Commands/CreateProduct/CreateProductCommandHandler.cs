using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Handler for creating a product
/// </summary>
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;
    private readonly ILogger<CreateProductCommandHandler> _logger;
    private readonly ICategoryRepository _categoryRepository;

    public CreateProductCommandHandler(IProductRepository productRepository, ILogger<CreateProductCommandHandler> logger,  ICategoryRepository categoryRepository)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _logger = logger;
    }
    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating product");
        // Check if SKU already exists
        var existingProd = await _productRepository.GetBySkuAsync(request.Sku, cancellationToken);
        if (existingProd != null)
        {
            _logger.LogWarning("Product with Sku {Sku} already exists", request.Sku);
            return Result<Guid>.Failure(new Error("Conflict",$"Product with Sku {request.Sku} already exists"));
        }
        // Validate category exists
        var isExist = await _categoryRepository.GetById(request.CategoryId, cancellationToken);
        if (isExist == null)
        {
            _logger.LogWarning("Category does not exist");
            return Result<Guid>.Failure(new Error("Conflict","Category does not exist"));
        }
        // Create Product entity using factory method
        var product = Product.Create(request.Name, request.Sku, request.Price, request.StockQuantity, request.CategoryId);
        // Add to repository
        await _productRepository.AddAsync(product, cancellationToken);
        
        // Return product ID
        _logger.LogInformation("Product created");
        return Result<Guid>.Success(product.Id);
    }
}
