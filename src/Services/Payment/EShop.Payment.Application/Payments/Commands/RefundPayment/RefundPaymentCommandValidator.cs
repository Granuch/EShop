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
    }
}
