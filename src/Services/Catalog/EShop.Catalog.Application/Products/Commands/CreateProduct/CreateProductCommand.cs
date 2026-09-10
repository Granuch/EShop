using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Command to create a new product.
/// Invalidates product list caches upon successful execution.
/// </summary>
public record CreateProductCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public Guid CategoryId { get; init; }

    /// <summary>
    /// Optional images to attach to the new product. The first one added becomes the main image.
    /// Null and omitted are both treated as "no images" — clients that serialize absent
    /// collections as explicit JSON null are not rejected.
    /// </summary>
    public IReadOnlyList<CreateProductImageRequest>? Images { get; init; }

    /// <summary>
    /// Optional key/value attributes to attach to the new product.
    /// Null and omitted are both treated as "no attributes".
    /// </summary>
    public IReadOnlyList<CreateProductAttributeRequest>? Attributes { get; init; }

    // Nothing to evict by exact key: a new product has no detail entry yet, and its category's
    // product list is paged since Stage 6 and so lives in the family below.
    public IEnumerable<string> CacheKeysToInvalidate => [];

    // DEBT-16. The list keys embed every filter/sort/page parameter, so they cannot be named for
    // exact-key invalidation; bumping the family version makes all of them — the per-category
    // pages included — unreachable in one operation. Declared here rather than called from the
    // handler so it is drained by CacheInvalidationBehavior — i.e. after the transaction commits.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
