using EShop.Ordering.Domain.Entities;
using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrders;

/// <summary>
/// Audit M3. The admin list had no validator: an unbounded page size loaded every order with every
/// item, a page number below 1 made a negative Skip, and an unknown status was silently dropped, so
/// <c>?status=Payed</c> returned every order as though it had been honoured.
/// </summary>
public class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    private static readonly string[] StatusNames = Enum.GetNames<OrderStatus>();

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
    }
}
