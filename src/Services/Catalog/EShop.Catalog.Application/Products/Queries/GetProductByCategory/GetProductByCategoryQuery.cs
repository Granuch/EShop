using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Entities;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public record GetProductByCategoryQuery :  IRequest<Result<List<ProductDto>>>
{
    public Guid CategoryId { get; init; }
}