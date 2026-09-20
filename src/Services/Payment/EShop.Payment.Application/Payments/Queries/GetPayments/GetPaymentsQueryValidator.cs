using FluentValidation;

namespace EShop.Payment.Application.Payments.Queries.GetPayments;

/// <summary>
/// An unbounded page size would load every payment ever recorded, and an unrecognised filter value must be an error
/// rather than a filter quietly reduced to "everything" — the same principle as Ordering's list validator.
/// The page bounds match <c>GetPaymentsByUserQueryValidator</c>'s, so the two payment lists refuse the same pages.
/// </summary>
public sealed class GetPaymentsQueryValidator : AbstractValidator<GetPaymentsQuery>
{
    public GetPaymentsQueryValidator()
    {
        PaymentFilterRules.Apply(this);

        RuleFor(x => x.EffectivePageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.")
            .OverridePropertyName(nameof(GetPaymentsQuery.PageNumber));

        RuleFor(x => x.EffectivePageSize)
            .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.")
            .OverridePropertyName(nameof(GetPaymentsQuery.PageSize));
    }
}
