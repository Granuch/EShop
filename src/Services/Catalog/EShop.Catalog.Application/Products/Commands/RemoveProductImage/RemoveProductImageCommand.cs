using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.RemoveProductImage;

/// <summary>
/// Command to remove an image from a product.
/// Removing the main image promotes the next image in gallery order; removing the last
/// image leaves the product with no main image.
/// </summary>
public record RemoveProductImageCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public Guid ImageId { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"product:{ProductId}"
    ];
}
