using EShop.BuildingBlocks.Application;
using EShop.Notification.Application.Notifications.Common;
using MediatR;

namespace EShop.Notification.Application.Notifications.Queries.GetNotificationStats;

/// <summary>
/// Sent / failed / queued over one window (Admin panel S12, endpoint #77). It carries the whole journal filter surface,
/// not just the dates, so "how many order-confirmation emails failed this week" is one request rather than a page of
/// rows the caller has to count.
/// </summary>
public sealed record GetNotificationStatsQuery : NotificationFilterQuery, IRequest<Result<NotificationStatsDto>>;
