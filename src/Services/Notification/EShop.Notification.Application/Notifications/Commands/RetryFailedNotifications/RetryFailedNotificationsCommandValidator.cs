using FluentValidation;

namespace EShop.Notification.Application.Notifications.Commands.RetryFailedNotifications;

/// <summary>
/// The journal filter's length rules and date order, plus the batch bound. Not <c>NotificationFilterRules</c>: that
/// helper validates a <c>NotificationFilterQuery</c>, whose status and has-error fields this command deliberately does
/// not have.
/// </summary>
public sealed class RetryFailedNotificationsCommandValidator : AbstractValidator<RetryFailedNotificationsCommand>
{
    public RetryFailedNotificationsCommandValidator()
    {
        RuleFor(x => x.EventType)
            .MaximumLength(200).WithMessage("EventType must not exceed 200 characters.");

        RuleFor(x => x.TemplateName)
            .MaximumLength(100).WithMessage("TemplateName must not exceed 100 characters.");

        RuleFor(x => x.UserId)
            .MaximumLength(100).WithMessage("UserId must not exceed 100 characters.");

        RuleFor(x => x.Email)
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.");

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .WithMessage("To must not be earlier than From")
            .When(x => x.From.HasValue && x.To.HasValue);

        // Through the Effective accessor, named back to what the caller sent, as the journal list does for pageSize.
        RuleFor(x => x.EffectiveLimit)
            .InclusiveBetween(1, RetryFailedNotificationsCommand.MaxRetryPerRequest)
            .WithMessage($"Limit must be between 1 and {RetryFailedNotificationsCommand.MaxRetryPerRequest}.")
            .OverridePropertyName(nameof(RetryFailedNotificationsCommand.Limit));
    }
}
