using FluentValidation;

namespace EShop.Payment.Application.Payments.Commands.RefundPayment;

public sealed class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId)
            .NotEmpty().WithMessage("PaymentId is required.");

        RuleFor(x => x.Amount)
            .Must(static amount => !amount.HasValue || amount.Value > 0)
            .WithMessage("Refund amount must be greater than 0.");

        // Payment audit Stage 10 (M6). Payments are stored at two decimals (decimal(18,2)). A request for 10.001 could
        // never equal a recorded amount, so it is refused as malformed, not as a partial refund.
        RuleFor(x => x.Amount)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Refund amount must have at most two decimal places.");

        // The same bound as the ErrorMessage column, where a reason would be recorded.
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Refund reason must not exceed 500 characters.");
    }
}
