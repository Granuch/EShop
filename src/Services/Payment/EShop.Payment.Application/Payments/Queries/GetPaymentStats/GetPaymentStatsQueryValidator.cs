using EShop.Payment.Domain.Interfaces;
using FluentValidation;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentStats;

/// <summary>
/// An unknown <c>groupBy</c> is an error rather than a silent fall back to Day: a dashboard asking for <c>week</c> and
/// getting daily buckets labelled as weeks is worse than an error it can show.
/// </summary>
public sealed class GetPaymentStatsQueryValidator : AbstractValidator<GetPaymentStatsQuery>
{
    private static readonly string[] GroupByNames = Enum.GetNames<PaymentStatsGroupBy>();

    public GetPaymentStatsQueryValidator()
    {
        RuleFor(x => x.GroupBy)
            .Must(groupBy => GroupByNames.Contains(groupBy, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"GroupBy must be one of: {string.Join(", ", GroupByNames)}")
            .When(x => !string.IsNullOrEmpty(x.GroupBy));

        // Three letters, as the column is. A longer value could never match a stored currency, so it is refused as
        // malformed rather than answering a window of zeroes that reads like "no payments yet".
        RuleFor(x => x.Currency)
            .Length(3).WithMessage("Currency must be a three-letter code.")
            .When(x => !string.IsNullOrWhiteSpace(x.Currency));

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .WithMessage("To must not be earlier than From")
            .When(x => x.From.HasValue && x.To.HasValue);
    }
}
