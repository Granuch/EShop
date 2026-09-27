using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Application.Notifications.Commands.RetryFailedNotifications;

/// <summary>
/// <para>
/// <b>Each row is dispatched on its own, and one refusal does not stop the rest.</b> The report is per row, so a
/// dispatch the bus refused is named in <c>FailedIds</c> rather than turning the whole batch into a 503 that hides the
/// ones that did go.
/// </para>
/// <para>
/// <b>Calling it twice in a row can send the same rows twice</b>, because a dispatched row stays Failed until its
/// consumer claims it. That is safe rather than merely tolerated: every copy lands on the same <c>EventId</c>, the
/// consumer's claim lets one attempt through, and the others find it Sending (retried later) or Sent (acknowledged,
/// nothing sent). No email is sent twice by it.
/// </para>
/// <para>Writes nothing itself, for the reason <c>ResendNotificationCommandHandler</c> gives.</para>
/// </summary>
public sealed class RetryFailedNotificationsCommandHandler
    : IRequestHandler<RetryFailedNotificationsCommand, Result<RetryFailedNotificationsResultDto>>
{
    private readonly INotificationQueryService _queryService;
    private readonly INotificationRedispatcher _redispatcher;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<RetryFailedNotificationsCommandHandler> _logger;

    public RetryFailedNotificationsCommandHandler(
        INotificationQueryService queryService,
        INotificationRedispatcher redispatcher,
        ICurrentUserContext currentUser,
        ILogger<RetryFailedNotificationsCommandHandler> logger)
    {
        _queryService = queryService;
        _redispatcher = redispatcher;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<RetryFailedNotificationsResultDto>> Handle(
        RetryFailedNotificationsCommand request,
        CancellationToken cancellationToken)
    {
        if (!_redispatcher.IsAvailable)
        {
            return Result<RetryFailedNotificationsResultDto>.Failure(new Error(
                NotificationErrors.BusUnavailableCode,
                "This Notification host runs no message bus, so a retry has nowhere to go."));
        }

        var limit = request.EffectiveLimit;
        var (candidates, matching) = await _queryService.GetRetryCandidatesAsync(
            request.ToFilter(), limit, cancellationToken);

        var dispatched = new List<Guid>(candidates.Count);
        var failed = new List<Guid>();

        foreach (var candidate in candidates)
        {
            if (!_redispatcher.CanRedispatch(candidate.EventType))
            {
                failed.Add(candidate.Id);
                continue;
            }

            try
            {
                await _redispatcher.RedispatchAsync(candidate.EventType, candidate.Payload, cancellationToken);
                dispatched.Add(candidate.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "The retry of notification {NotificationId} could not be queued.", candidate.Id);
                failed.Add(candidate.Id);
            }
        }

        _logger.LogInformation(
            "Operator {ActorId} queued {Dispatched} failed notification(s) for retry ({Failed} refused, {Matching} matching, limit {Limit}).",
            _currentUser.UserId, dispatched.Count, failed.Count, matching, limit);

        return Result<RetryFailedNotificationsResultDto>.Success(
            new RetryFailedNotificationsResultDto(matching, limit, dispatched, failed));
    }
}
