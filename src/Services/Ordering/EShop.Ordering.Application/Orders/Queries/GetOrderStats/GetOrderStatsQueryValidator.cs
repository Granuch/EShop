using EShop.Ordering.Application.Abstractions;
using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderStats;

/// <summary>
/// An unknown <c>groupBy</c> is an error rather than a silent fall back to Day: a dashboard asking
/// for <c>week</c> and getting daily buckets labelled as weeks is worse than an error it can show.
/// </summary>
public class GetOrderStatsQueryValidator : AbstractValidator<GetOrderStatsQuery>
{
    private static readonly string[] GroupByNames = Enum.GetNames<OrderStatsGroupBy>();

    public GetOrderStatsQueryValidator()
    {
        RuleFor(x => x.GroupBy)
            .Must(groupBy => GroupByNames.Contains(groupBy, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"GroupBy must be one of: {string.Join(", ", GroupByNames)}")
            .When(x => !string.IsNullOrEmpty(x.GroupBy));

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .WithMessage("To must not be earlier than From")
            .When(x => x.From.HasValue && x.To.HasValue);
    }
}
