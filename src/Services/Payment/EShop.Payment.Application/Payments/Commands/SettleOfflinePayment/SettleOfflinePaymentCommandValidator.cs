using FluentValidation;

namespace EShop.Payment.Application.Payments.Commands.SettleOfflinePayment;

public sealed class SettleOfflinePaymentCommandValidator : AbstractValidator<SettleOfflinePaymentCommand>
{
    public SettleOfflinePaymentCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("OrderId is required.");

        RuleFor(x => x.Reference)
            .NotEmpty().WithMessage("Reference is required.")
            .MaximumLength(SettleOfflinePaymentCommand.MaxReferenceLength)
            .WithMessage($"Reference must not exceed {SettleOfflinePaymentCommand.MaxReferenceLength} characters.");
    }
}
