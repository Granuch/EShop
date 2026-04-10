using FluentValidation;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

public sealed class CreatePaymentIntentCommandValidator : AbstractValidator<CreatePaymentIntentCommand>
{
    public CreatePaymentIntentCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("OrderId is required.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0.");

        RuleFor(x => x.Currency)
            .Must(static currency => string.IsNullOrWhiteSpace(currency) || currency.Length == 3)
            .WithMessage("Currency must be a 3-letter ISO code.");
    }
}
