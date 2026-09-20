using FluentValidation;

namespace EShop.Payment.Application.Payments.Queries.ExportPayments;

/// <summary>
/// The same filter rules as the admin list, and nothing about paging — the export has none. Sharing
/// <c>PaymentFilterRules</c> is what stops the two surfaces disagreeing about which status names exist.
/// </summary>
public sealed class ExportPaymentsQueryValidator : AbstractValidator<ExportPaymentsQuery>
{
    public ExportPaymentsQueryValidator()
    {
        PaymentFilterRules.Apply(this);
    }
}
