using EShop.BuildingBlocks.Application;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Interfaces;
using MediatR;

namespace EShop.Notification.Application.Notifications.Queries.GetNotificationById;

public sealed class GetNotificationByIdQueryHandler
    : IRequestHandler<GetNotificationByIdQuery, Result<NotificationDetailDto>>
{
    /// <summary>The error code the endpoint maps to 404. Named here so the test and the endpoint agree on one string.</summary>
    public const string NotFoundCode = "Notification.NotFound";

    private readonly INotificationQueryService _queryService;

    public GetNotificationByIdQueryHandler(INotificationQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<NotificationDetailDto>> Handle(
        GetNotificationByIdQuery request,
        CancellationToken cancellationToken)
    {
        var log = await _queryService.FindAsync(request.Id, cancellationToken);

        return log is null
            ? Result<NotificationDetailDto>.Failure(
                new Error(NotFoundCode, $"Notification {request.Id} was not found."))
            : Result<NotificationDetailDto>.Success(log.ToDetailDto());
    }
}
