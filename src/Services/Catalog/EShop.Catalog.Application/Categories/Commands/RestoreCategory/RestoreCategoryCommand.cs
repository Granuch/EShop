using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.RestoreCategory;

/// <summary>
/// Command to bring a soft-deleted category back (Admin panel S5, endpoint #52). Category delete
/// became a soft delete in Catalog audit Stage 8 (M12), but nothing could undo it.
/// </summary>
public record RestoreCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid CategoryId { get; init; }

    public IEnumerable<string> CacheKeysToInvalidate => [CategoryCacheKeys.Detail(CategoryId)];

    /// <summary>
    /// Both families. The tree read changes, and so does every product list: a restored category's
    /// products become reachable through <c>GET /categories/{id}/products</c> again.
    /// </summary>
    public IEnumerable<string> CacheFamiliesToInvalidate =>
        [CategoryCacheFamilies.CategoryList, ProductCacheFamilies.ProductList];
}
