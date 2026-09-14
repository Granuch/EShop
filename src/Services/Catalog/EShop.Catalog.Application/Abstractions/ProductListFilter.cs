namespace EShop.Catalog.Application.Abstractions;

/// <summary>
/// The filters every product list read applies, whatever its paging model.
///
/// <para>
/// One record rather than five loose parameters because there are now three list reads —
/// <c>GET /products</c> (offset), <c>GET /products/newest</c> (keyset) and
/// <c>GET /categories/{id}/products</c> — and they must filter identically. In particular the
/// published-only rule lives in <see cref="IncludeUnpublished"/>, so a new read path cannot forget
/// it without visibly passing <c>true</c>.
/// </para>
/// </summary>
/// <param name="IncludeUnpublished">
/// D1 / H5a. False restricts the result to <c>ProductStatus.Active</c>. Deliberately has no
/// default: set it from the caller's role at the endpoint, never from a bound request property.
/// </param>
public sealed record ProductListFilter(
    Guid? CategoryId,
    string? SearchTerm,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool IncludeUnpublished);
