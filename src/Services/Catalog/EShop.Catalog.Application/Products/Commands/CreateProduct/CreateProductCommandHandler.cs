using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Telemetry;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Handler for creating a product
/// </summary>
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        using var activity = CatalogActivitySource.Source.StartActivity("Catalog.CreateProduct");
        activity?.SetTag("product.sku", request.Sku);
        activity?.SetTag("product.category_id", request.CategoryId.ToString());

        var existingProduct = await _productRepository.GetBySkuAsync(request.Sku, cancellationToken);
        if (existingProduct != null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "sku_conflict");
            return Result<Guid>.Failure(new Error("Product.SkuConflict", $"Product with SKU '{request.Sku}' already exists."));
        }

        var category = await _categoryRepository.GetById(request.CategoryId, cancellationToken);
        if (category == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "category_not_found");
            return Result<Guid>.Failure(new Error("Category.NotFound", $"Category with ID '{request.CategoryId}' was not found."));
        }

        var product = Product.Create(request.Name, request.Sku, request.Price, request.StockQuantity, request.CategoryId);

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("product.id", product.Id.ToString());

        return Result<Guid>.Success(product.Id);
    }
}
