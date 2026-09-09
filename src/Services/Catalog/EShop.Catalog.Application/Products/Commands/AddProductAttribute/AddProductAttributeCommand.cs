using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.AddProductAttribute;

/// <summary>
/// Command to add a key/value attribute to an existing product. Returns the new attribute's ID.
/// Attributes are add-only — there is no update or remove command by design.
/// </summary>
public record AddProductAttributeCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"product:{ProductId}"
    ];

    // DEBT-16. products:list:* keys embed every filter/sort/page parameter and cannot be named,
    // so the family version is bumped instead. The handler still adds products:category:{id} to
    // ICacheInvalidationContext, because the command does not know the CategoryId.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
