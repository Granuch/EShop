using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Interfaces;
using MediatR;

namespace EShop.Notification.Application.Notifications.Queries.GetNotifications;

public sealed class GetNotificationsQueryHandler
    : IRequestHandler<GetNotificationsQuery, Result<PagedResult<NotificationSummaryDto>>>
{
    private readonly INotificationQueryService _queryService;

    public GetNotificationsQueryHandler(INotificationQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<PagedResult<NotificationSummaryDto>>> Handle(
        GetNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.EffectivePageNumber;
        var pageSize = request.EffectivePageSize;

        var (logs, totalCount) = await _queryService.GetPageAsync(
            request.ToFilter(),
            pageNumber,
            pageSize,
            cancellationToken);

        return Result<PagedResult<NotificationSummaryDto>>.Success(PagedResult<NotificationSummaryDto>.Create(
            logs.Select(log => log.ToSummaryDto()).ToList(),
            pageNumber,
            pageSize,
            totalCount));
    }
}
