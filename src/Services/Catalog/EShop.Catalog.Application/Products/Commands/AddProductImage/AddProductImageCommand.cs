using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AddProductImage;

/// <summary>
/// Command to add an image to an existing product. Returns the new image's ID.
/// </summary>
public record AddProductImageCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int DisplayOrder { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"product:{ProductId}"
    ];
}
