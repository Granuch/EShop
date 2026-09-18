using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Catalog.Application.Products;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.MoveCategory;

/// <summary>
/// Command to re-parent a category (Admin panel S5, endpoint #50). A category's parent was fixed at
/// creation from Catalog audit Stage 8 onward, because the cycle check that existed then walked the
/// in-memory navigation and could not be trusted — so reorganising a tree meant recreating it.
/// </summary>
/// <remarks>
/// <c>NewParentCategoryId</c> is nullable and null is meaningful: it promotes the category to a
/// root. That makes "omitted" and "explicitly null" indistinguishable in JSON, which is the BUG-09
/// ambiguity — resolved here by the endpoint requiring the property to be present
/// (<c>MoveCategoryRequest</c> is a dedicated body with one field, so an omitted body is a 400
/// rather than a silent promotion to root).
/// </remarks>
public record MoveCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public Guid CategoryId { get; init; }
    public Guid? NewParentCategoryId { get; init; }

    /// <summary>
    /// The detail entries that embed this category. Its own, plus the old and new parents' — each
    /// lists it (or stops listing it) under <c>ChildCategories</c>. The old parent's id is not on
    /// the command, so the handler adds it through <c>ICacheInvalidationContext</c> once it has
    /// loaded the category; this property covers the two it can name up front.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => NewParentCategoryId is { } newParentId
        ? [CategoryCacheKeys.Detail(CategoryId), CategoryCacheKeys.Detail(newParentId)]
        : [CategoryCacheKeys.Detail(CategoryId)];

    /// <summary>
    /// Two families. The tree read obviously changes shape. The product list does too, and that one
    /// is easy to miss: <c>GET /categories/{id}/products</c> lives in <c>products:list</c>, and
    /// moving a category changes which products a caller browsing the parent can reach.
    /// </summary>
    public IEnumerable<string> CacheFamiliesToInvalidate =>
        [CategoryCacheFamilies.CategoryList, ProductCacheFamilies.ProductList];
}
