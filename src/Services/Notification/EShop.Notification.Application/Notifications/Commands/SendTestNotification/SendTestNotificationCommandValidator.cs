using System.Net.Mail;
using FluentValidation;

namespace EShop.Notification.Application.Notifications.Commands.SendTestNotification;

public sealed class SendTestNotificationCommandValidator : AbstractValidator<SendTestNotificationCommand>
{
    public SendTestNotificationCommandValidator()
    {
        // MailAddress, not FluentValidation's EmailAddress(): the address is handed to RecipientAddress, which parses it
        // with MailAddress and throws on anything it rejects. EmailAddress() only checks for an '@', so an address it
        // passes could still reach the handler and fail there as a 500 rather than here as a 400.
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.")
            .Must(email => MailAddress.TryCreate(email, out _)).WithMessage("Email is not a valid address.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email), ApplyConditionTo.CurrentValidator);

        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");
    }
}
