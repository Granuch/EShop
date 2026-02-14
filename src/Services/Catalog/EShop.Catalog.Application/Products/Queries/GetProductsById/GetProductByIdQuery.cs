using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

public record GetProductByIdQuery : IRequest<Result<ProductDto>>
{
    public Guid ProductId { get; init; }
}