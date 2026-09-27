using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Notification.Application.Notifications.Common;
using MediatR;

namespace EShop.Notification.Application.Notifications.Queries.GetNotifications;

/// <summary>
/// The admin notification journal (<c>GET /api/v1/notifications</c>, Admin panel S12, endpoint #70). Notification had
/// no HTTP surface of any kind before this stage, so every delivery failure was visible only in Seq or by querying the
/// database by hand.
/// </summary>
public sealed record GetNotificationsQuery : NotificationFilterQuery, IRequest<Result<PagedResult<NotificationSummaryDto>>>
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 20;
}
