using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ReplaceProductAttributes;

/// <summary>
/// A single key/value attribute in a whole-set replacement.
/// </summary>
/// <remarks>
/// Deliberately not <c>CreateProductAttributeRequest</c>, matching the convention that the
/// inline-create and sub-resource slices keep independent request types and validators, so a limit
/// can be changed on one without silently changing the other.
/// </remarks>
public record ReplaceProductAttributeRequest
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Command to set a product's attributes to exactly the supplied set (Admin panel S3) — what an
/// admin "edit attributes" grid submits when it saves.
/// </summary>
/// <remarks>
/// <para>
/// <c>Attributes</c> is nullable because System.Text.Json writes an absent optional collection as
/// explicit <c>null</c>, which would overwrite a <c>= []</c> initializer. An explicit empty list is
/// a legitimate request meaning "this product has no attributes", so the validator requires the
/// property to be present but allows it to be empty — which makes this the one way to clear the
/// whole set at once.
/// </para>
/// <para>
/// The handler does not clear and re-add. See <c>Product.ReplaceAttributes</c> for why: EF Core
/// does not guarantee it emits a DELETE before an INSERT of the same key in one SaveChanges, and
/// against M1's non-deferrable unique index that would abort the batch with 23505.
/// </para>
/// </remarks>
public record ReplaceProductAttributesCommand : IRequest<Result>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Product";

    string? IAuditedCommand.AuditEntityId => ProductId.ToString();

    public Guid ProductId { get; init; }
    public IReadOnlyList<ReplaceProductAttributeRequest>? Attributes { get; init; }

    // Both detail variants: the public one and the admin one that includes drafts. Evicting
    // only the first leaves the other serving stale data for its full TTL, with nothing to
    // show for it. See ProductCacheKeys.
    public IEnumerable<string> CacheKeysToInvalidate =>
        ProductCacheKeys.AllDetailVariants(ProductId);

    // DEBT-16 — see UpdateProductAttributeCommand for why the list family is bumped even though
    // today's list projection carries no attributes.
    public IEnumerable<string> CacheFamiliesToInvalidate => [ProductCacheFamilies.ProductList];
}
