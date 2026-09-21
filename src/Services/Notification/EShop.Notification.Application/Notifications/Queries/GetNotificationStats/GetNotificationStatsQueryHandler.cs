using EShop.BuildingBlocks.Application;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Interfaces;
using MediatR;

namespace EShop.Notification.Application.Notifications.Queries.GetNotificationStats;

public sealed class GetNotificationStatsQueryHandler
    : IRequestHandler<GetNotificationStatsQuery, Result<NotificationStatsDto>>
{
    private readonly INotificationQueryService _queryService;

    public GetNotificationStatsQueryHandler(INotificationQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<NotificationStatsDto>> Handle(
        GetNotificationStatsQuery request,
        CancellationToken cancellationToken)
    {
        var stats = await _queryService.GetStatsAsync(request.ToFilter(), cancellationToken);

        return Result<NotificationStatsDto>.Success(stats.ToDto());
    }
}
