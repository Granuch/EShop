using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.DeleteProduct;

public record DeleteProductCommand : IRequest<Result>, ICacheInvalidatingCommand
{
    public Guid ProductId { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"product:{ProductId}"
    ];
}