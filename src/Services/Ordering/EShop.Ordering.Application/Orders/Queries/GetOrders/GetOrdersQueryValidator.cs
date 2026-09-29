using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Domain.Entities;
using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrders;

/// <summary>
/// Audit M3. The admin list had no validator: an unbounded page size loaded every order with every
/// item, a page number below 1 made a negative Skip, and an unknown status was silently dropped, so
/// <c>?status=Payed</c> returned every order as though it had been honoured.
///
/// <para>
/// Admin panel S8 extended it to the new filters, on the same principle: an unrecognised
/// <c>sortBy</c>, an inverted range or a status typo anywhere in <c>?statuses=</c> is an error, never
/// a filter quietly reduced to "everything".
/// </para>
/// </summary>
public class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    private static readonly string[] StatusNames = Enum.GetNames<OrderStatus>();
    private static readonly string[] SortNames = Enum.GetNames<OrderSortBy>();

    /// <summary>
    /// Long enough for an order id or a Stripe intent id, short enough that the ILIKE cannot be
    /// handed a megabyte. Nothing longer can match either column.
    /// </summary>
    public const int MaxSearchLength = 200;

    public GetOrdersQueryValidator()
    {
        RuleFor(x => x.EffectivePageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1")
            .OverridePropertyName(nameof(GetOrdersQuery.PageNumber));

        RuleFor(x => x.EffectivePageSize)
            .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100")
            .OverridePropertyName(nameof(GetOrdersQuery.PageSize));

        // By name only. Enum.TryParse also accepts "7" or "-1", which would reach the query as a status
        // no order can have and return an empty page instead of an error.
        RuleFor(x => x.Status)
            .Must(status => StatusNames.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Status must be one of: {string.Join(", ", StatusNames)}")
            .When(x => !string.IsNullOrEmpty(x.Status));

        // Every element, not just the first: one typo among five names would otherwise narrow the
        // list to the four that parsed, which reads as a working filter.
        RuleForEach(x => x.Statuses)
            .Must(status => StatusNames.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Statuses must each be one of: {string.Join(", ", StatusNames)}")
            .When(x => x.Statuses is { Length: > 0 });

        RuleFor(x => x.SortBy)
            .Must(sortBy => SortNames.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortNames)}")
            .When(x => !string.IsNullOrEmpty(x.SortBy));

        RuleFor(x => x.Search)
            .MaximumLength(MaxSearchLength)
            .WithMessage($"Search must not exceed {MaxSearchLength} characters");

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .WithMessage("To must not be earlier than From")
            .When(x => x.From.HasValue && x.To.HasValue);

        RuleFor(x => x.MinTotal)
            .GreaterThanOrEqualTo(0).WithMessage("MinTotal must not be negative")
            .When(x => x.MinTotal.HasValue);

        RuleFor(x => x.MaxTotal)
            .GreaterThanOrEqualTo(x => x.MinTotal!.Value)
            .WithMessage("MaxTotal must not be less than MinTotal")
            .When(x => x.MinTotal.HasValue && x.MaxTotal.HasValue);
    }
}
