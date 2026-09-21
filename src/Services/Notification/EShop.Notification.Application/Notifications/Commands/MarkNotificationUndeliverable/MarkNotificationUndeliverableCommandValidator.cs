using FluentValidation;

namespace EShop.Notification.Application.Notifications.Commands.MarkNotificationUndeliverable;

public sealed class MarkNotificationUndeliverableCommandValidator : AbstractValidator<MarkNotificationUndeliverableCommand>
{
    public MarkNotificationUndeliverableCommandValidator()
    {
        // NotEmpty rejects whitespace-only too, which the domain would otherwise refuse with an ArgumentException — a 500
        // for what is a caller's mistake.
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .MaximumLength(MarkNotificationUndeliverableCommand.MaxReasonLength)
            .WithMessage($"Reason must not exceed {MarkNotificationUndeliverableCommand.MaxReasonLength} characters.");
    }
}
