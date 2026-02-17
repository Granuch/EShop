using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Command to create a new product.
/// Invalidates product list caches upon successful execution.
/// </summary>
public record CreateProductCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public Guid CategoryId { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"products:category:{CategoryId}"
    ];
}
