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

        // Payment audit Stage 10 (M6). The e-mail goes to Stripe, which refuses a malformed or overlong one with a 400.
        // That became our 500. 254 characters is the longest address SMTP allows. Omitted or blank means none, which the
        // trailing When keeps valid: it guards the whole chain.
        RuleFor(x => x.Email)
            .MaximumLength(254).WithMessage("Email must not exceed 254 characters.")
            .EmailAddress().WithMessage("Email is not a valid e-mail address.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
