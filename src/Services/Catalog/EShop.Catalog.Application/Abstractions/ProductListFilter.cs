using EShop.Catalog.Domain.Entities;

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
/// <param name="Status">
/// Admin panel S4. Narrows to one <c>ProductStatus</c>. It <b>ANDs with</b>
/// <paramref name="IncludeUnpublished"/> rather than overriding it, so a public caller asking for
/// <c>Draft</c> gets an empty page rather than the unpublished catalogue.
/// </param>
/// <param name="HasDiscount">
/// Admin panel S4. True selects products with a discount price set, false those without.
/// </param>
/// <param name="StockBelow">
/// Admin panel S4. Strictly less than, so <c>stockBelow=1</c> is "out of stock". Also what the
/// low-stock admin read passes.
/// </param>
/// <param name="CreatedFrom">Admin panel S4. Inclusive lower bound on <c>CreatedAt</c> (UTC).</param>
/// <param name="CreatedTo">Admin panel S4. Inclusive upper bound on <c>CreatedAt</c> (UTC).</param>
/// <param name="DeletedOnly">
/// Admin panel S4. Lifts the <c>!IsDeleted</c> global query filter <b>and</b> restricts the result
/// to soft-deleted products — one flag rather than a pair, because "lift the filter" and "show only
/// deleted" are never wanted separately here and two booleans would have a meaningless fourth
/// combination. <b>Admin-only, and set at the endpoint</b> like
/// <paramref name="IncludeUnpublished"/> — never from a bound request property.
/// </param>
/// <remarks>
/// The six S4 parameters are positional with defaults so the three existing call sites keep
/// compiling unchanged. Adding one <i>without</i> a default, or inserting one before
/// <c>IncludeUnpublished</c>, breaks them — and inserting a defaulted parameter in the middle
/// silently rebinds every positional argument after it, which is the shape that produced the
/// CS1503 cascade in S2.
/// </remarks>
public sealed record ProductListFilter(
    Guid? CategoryId,
    string? SearchTerm,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool IncludeUnpublished,
    ProductStatus? Status = null,
    bool? HasDiscount = null,
    int? StockBelow = null,
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null,
    bool DeletedOnly = false);
