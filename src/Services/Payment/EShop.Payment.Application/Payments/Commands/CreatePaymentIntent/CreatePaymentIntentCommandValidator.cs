using FluentValidation;

namespace EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

public sealed class CreatePaymentIntentCommandValidator : AbstractValidator<CreatePaymentIntentCommand>
{
    public CreatePaymentIntentCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("OrderId is required.");

        RuleFor(x => x.RequesterId)
            .NotEmpty().WithMessage("The requesting user is required.")
            .When(x => !x.RequesterIsAdmin);
    }
}
