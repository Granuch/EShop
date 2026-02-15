using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public record UpdateProductCommand : IRequest<Result>, ICacheInvalidatingCommand
{
    public Guid ProductId { get; init; }
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"product:{ProductId}"
    ];
}