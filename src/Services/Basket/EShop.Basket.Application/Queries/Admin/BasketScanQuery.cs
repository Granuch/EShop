using FluentValidation;

namespace EShop.Basket.Application.Queries.Admin;

/// <summary>
/// The paging surface <c>/admin/carts</c> and <c>/admin/abandoned</c> share. Bound with <c>[AsParameters]</c>, so every
/// value type is nullable: a plain <c>int</c> would make the parameter required, and a request omitting it would fail
/// binding with a 400 that blames a JSON body the GET does not have.
/// </summary>
public abstract record BasketScanQuery
{
    public const int DefaultPageSize = 20;

    /// <summary>
    /// The most baskets one page returns — the page-size bound the plan names for S14. Each basket on a page is a
    /// document read and deserialized, so this bounds the request's work as well as its response.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>Omitted: from the start of the walk. Otherwise exactly the <c>nextCursor</c> a previous page returned.</summary>
    public string? Cursor { get; init; }

    public int? PageSize { get; init; }

    public int EffectivePageSize => PageSize ?? DefaultPageSize;
}

/// <summary>
/// The rules both walks share, applied from each validator. A validator for the base record would never run:
/// FluentValidation resolves <c>IValidator&lt;T&gt;</c> for the request's own type (Admin panel S10's lesson).
/// </summary>
public static class BasketScanRules
{
    public static void Apply<T>(AbstractValidator<T> validator) where T : BasketScanQuery
    {
        validator.RuleFor(x => x.PageSize)
            .InclusiveBetween(1, BasketScanQuery.MaxPageSize)
            .When(x => x.PageSize.HasValue)
            .WithMessage($"pageSize must be between 1 and {BasketScanQuery.MaxPageSize}.");

        validator.RuleFor(x => x.Cursor)
            .Must(cursor => BasketScanCursor.TryParse(cursor, out _))
            .WithMessage("cursor must be a nextCursor value this API returned.");
    }
}
