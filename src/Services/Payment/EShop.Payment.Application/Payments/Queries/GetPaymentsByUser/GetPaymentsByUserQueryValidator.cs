using FluentValidation;

namespace EShop.Payment.Application.Payments.Queries.GetPaymentsByUser;

/// <summary>The same page rules as Ordering's per-user order list (Payment audit S10, D10).</summary>
public sealed class GetPaymentsByUserQueryValidator : AbstractValidator<GetPaymentsByUserQuery>
{
    public GetPaymentsByUserQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required.");

        // Named as sent: a client never sees an "EffectivePageSize" parameter.
        RuleFor(x => x.EffectivePageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.")
            .OverridePropertyName(nameof(GetPaymentsByUserQuery.PageNumber));

        RuleFor(x => x.EffectivePageSize)
            .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.")
            .OverridePropertyName(nameof(GetPaymentsByUserQuery.PageSize));
    }
}
