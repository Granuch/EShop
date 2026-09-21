using FluentValidation;

namespace EShop.Notification.Application.Notifications.Queries.GetNotifications;

/// <summary>
/// An unbounded page size would load every notification the retention window still holds, and an unrecognised filter
/// value must be an error rather than a filter quietly reduced to "everything" — the same principle as Payment's and
/// Ordering's list validators.
/// </summary>
public sealed class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator()
    {
        NotificationFilterRules.Apply(this);

        // Validated through the Effective* accessors, with the property name overridden back to the one the caller
        // actually sent, so "?pageSize=0" names pageSize rather than an accessor nobody can set.
        RuleFor(x => x.EffectivePageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.")
            .OverridePropertyName(nameof(GetNotificationsQuery.PageNumber));

        RuleFor(x => x.EffectivePageSize)
            .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.")
            .OverridePropertyName(nameof(GetNotificationsQuery.PageSize));
    }
}
