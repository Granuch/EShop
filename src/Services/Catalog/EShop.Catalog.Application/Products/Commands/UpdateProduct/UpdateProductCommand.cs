using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public record UpdateProductCommand : IRequest<Result>
{
    public Guid ProductId { get; init; }
    public  decimal Price { get; init; }
    public int StockQuantity { get; init; }
}