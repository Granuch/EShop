using FluentValidation;

namespace EShop.Notification.Application.Notifications.Queries.GetNotificationStats;

/// <summary>The same filter rules the list uses, so the two cannot accept different values for the same parameter.</summary>
public sealed class GetNotificationStatsQueryValidator : AbstractValidator<GetNotificationStatsQuery>
{
    public GetNotificationStatsQueryValidator()
    {
        NotificationFilterRules.Apply(this);
    }
}
