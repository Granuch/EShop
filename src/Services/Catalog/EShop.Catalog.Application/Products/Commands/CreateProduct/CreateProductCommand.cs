using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Command to create a new product
/// </summary>
public record CreateProductCommand : IRequest<Result<Guid>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public Guid CategoryId { get; init; }
}
