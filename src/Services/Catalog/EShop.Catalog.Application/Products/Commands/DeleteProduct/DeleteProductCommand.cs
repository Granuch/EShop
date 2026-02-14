using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.DeleteProduct;

public record DeleteProductCommand : IRequest<Result>
{
    public Guid ProductId { get; init; }
}