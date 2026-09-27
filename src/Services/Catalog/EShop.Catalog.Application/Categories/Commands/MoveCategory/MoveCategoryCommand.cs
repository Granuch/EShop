using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
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
/// (<c>MoveCategoryRequest</c> is a dedicated body with one field; an omitted body is a 400
/// <c>MalformedRequest</c>, and since F-38 a body without the property is a 400
/// <c>ValidationError</c> keyed <c>newParentCategoryId</c>, rather than a silent promotion to root).
/// </remarks>
public record MoveCategoryCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Category";

    string? IAuditedCommand.AuditEntityId => CategoryId.ToString();

    public Guid CategoryId { get; init; }
    public Guid? NewParentCategoryId { get; init; }

    /// <summary>
    /// Empty on purpose. A move changes the subtree of every ancestor on both sides — old and new —
    /// and the detail read carries the whole subtree (F-37, frontend-contracts R5), so no list of
    /// exact keys could be complete. Both category reads are versioned in
    /// <see cref="CategoryCacheFamilies.CategoryList"/>, and the bump below covers them.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [];

    /// <summary>
    /// Two families. The tree read obviously changes shape. The product list does too, and that one
    /// is easy to miss: <c>GET /categories/{id}/products</c> lives in <c>products:list</c>, and
    /// moving a category changes which products a caller browsing the parent can reach.
    /// </summary>
    public IEnumerable<string> CacheFamiliesToInvalidate =>
        [CategoryCacheFamilies.CategoryList, ProductCacheFamilies.ProductList];
}
