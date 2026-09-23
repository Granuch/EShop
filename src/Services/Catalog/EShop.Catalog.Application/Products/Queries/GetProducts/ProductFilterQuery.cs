using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Entities;
using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// The filter and sort surface of the admin product list, shared by <see cref="GetProductsQuery"/> and
/// <c>ExportProductsQuery</c> (admin panel S16, endpoint #48 "current filters").
/// </summary>
/// <remarks>
/// <para>
/// <b>A base record rather than two copies of eleven properties.</b> <c>[AsParameters]</c> cannot nest a record, so the
/// alternative is a second filter surface that drifts — and an export that quietly stopped honouring <c>?Status=</c>
/// still returns a perfectly well-formed CSV of the wrong rows, which nobody notices. <c>[AsParameters]</c> does bind
/// inherited properties (Payment's <c>PaymentFilterQuery</c> established that). <see cref="ToFilter"/> is the one place the
/// properties become a <see cref="ProductListFilter"/>, for the same reason.
/// </para>
/// <para>
/// Every value type is nullable: <c>[AsParameters]</c> treats a non-nullable one as a REQUIRED query-string parameter.
/// </para>
/// </remarks>
public abstract record ProductFilterQuery
{
    public Guid? CategoryId { get; init; }
    public string? SearchTerm { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public ProductSortBy? SortBy { get; init; }
    public bool? IsDescending { get; init; }

    // Admin panel S4 — four admin list filters. Every one is nullable, including the bool and the
    // int: [AsParameters] treats a non-nullable value type as a REQUIRED query-string parameter, so
    // a plain `bool HasDiscount` would make every request that omits it fail binding, with a 400
    // reading "The request body is not valid JSON" on a GET that has no body. Nineteen tests went
    // red at once the last time that happened.
    //
    // These are NOT restricted to admins and do not need to be: Status ANDs with the published-only
    // rule rather than replacing it (see ProductQueryService.ApplyFilter), so a public caller
    // filtering for Draft gets an empty page. Visibility itself is decided at the endpoint and passed
    // to ToFilter, never bound.
    public ProductStatus? Status { get; init; }
    public bool? HasDiscount { get; init; }
    public int? StockBelow { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }

    public ProductSortBy EffectiveSortBy => SortBy ?? ProductSortBy.Name;
    public bool EffectiveIsDescending => IsDescending ?? false;

    /// <summary>
    /// The filter every read built on this record queries with.
    /// </summary>
    /// <param name="includeUnpublished">
    /// Decided from the caller's role by the endpoint or handler — deliberately a parameter, not a property, so no
    /// query-string value can reach it.
    /// </param>
    /// <remarks>
    /// <b>Dates are coerced to UTC here</b> (S16). A query-string date with no zone — <c>?CreatedFrom=2026-09-01</c>,
    /// which is what an admin URL looks like — binds as <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses to
    /// send one as a <c>timestamp with time zone</c> parameter, so the most obvious filter was a 500. Ordering's
    /// <c>QueryEnums.AsUtc</c> is the precedent; before S16 Catalog's list worked only for a caller who remembered the
    /// <c>Z</c>.
    /// </remarks>
    public ProductListFilter ToFilter(bool includeUnpublished) => new(
        CategoryId,
        SearchTerm,
        MinPrice,
        MaxPrice,
        includeUnpublished,
        Status: Status,
        HasDiscount: HasDiscount,
        StockBelow: StockBelow,
        CreatedFrom: AsUtc(CreatedFrom),
        CreatedTo: AsUtc(CreatedTo));

    private static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        var unspecified => DateTime.SpecifyKind(unspecified.Value, DateTimeKind.Utc)
    };
}

/// <summary>
/// The validation rules of <see cref="ProductFilterQuery"/>, applied by every validator of a derived query.
/// </summary>
/// <remarks>
/// A static helper generic over the derived type, not a base validator: FluentValidation resolves an
/// <c>IValidator&lt;T&gt;</c> of the request's own type, so a validator registered for the base record would be resolved
/// for no request and silently never run (the Payment S10 lesson).
/// </remarks>
public static class ProductFilterRules
{
    public static void Apply<T>(AbstractValidator<T> validator) where T : ProductFilterQuery
    {
        validator.RuleFor(x => x.MinPrice)
            .GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue)
            .WithMessage("Minimum price cannot be negative");

        validator.RuleFor(x => x.MaxPrice)
            .GreaterThan(x => x.MinPrice ?? 0).When(x => x.MaxPrice.HasValue && x.MinPrice.HasValue)
            .WithMessage("Maximum price must be greater than minimum price");

        validator.RuleFor(x => x.SearchTerm)
            .MinimumLength(2).When(x => !string.IsNullOrEmpty(x.SearchTerm))
            .WithMessage("Search term must be at least 2 characters")
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.SearchTerm))
            .WithMessage("Search term must not exceed 200 characters");
    }
}
