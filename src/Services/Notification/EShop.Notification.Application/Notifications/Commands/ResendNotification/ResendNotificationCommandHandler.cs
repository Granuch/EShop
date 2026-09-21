using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Application.Notifications.Commands.ResendNotification;

/// <summary>
/// <para>
/// <b>Writes nothing.</b> The row is read AsNoTracking and left alone: the consumer that receives the resent event
/// claims it exactly as it would a redelivery (Pending/Failed/expired Sending → Sending → Sent or Failed). Changing the
/// row here as well would give the operator's request a second, competing claim on it.
/// </para>
/// <para>
/// The checks below are the same the consumer applies — a final row is skipped, a live attempt is left alone — made
/// here too so the operator hears <i>why</i> nothing will happen, instead of a 202 for a message the consumer then
/// quietly acknowledges. They are advisory, not the guard: between this read and the consumer's claim anything may
/// change, and the consumer's own claim, under the row version, is what decides.
/// </para>
/// </summary>
public sealed class ResendNotificationCommandHandler
    : IRequestHandler<ResendNotificationCommand, Result<NotificationDetailDto>>
{
    private readonly INotificationQueryService _queryService;
    private readonly INotificationRedispatcher _redispatcher;
    private readonly ICurrentUserContext _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ResendNotificationCommandHandler> _logger;

    public ResendNotificationCommandHandler(
        INotificationQueryService queryService,
        INotificationRedispatcher redispatcher,
        ICurrentUserContext currentUser,
        TimeProvider timeProvider,
        ILogger<ResendNotificationCommandHandler> logger)
    {
        _queryService = queryService;
        _redispatcher = redispatcher;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<NotificationDetailDto>> Handle(
        ResendNotificationCommand request,
        CancellationToken cancellationToken)
    {
        var log = await _queryService.FindAsync(request.Id, cancellationToken);
        if (log is null)
        {
            return Result<NotificationDetailDto>.Failure(NotificationErrors.NotFound(request.Id));
        }

        var blocker = log.ResendBlockerAt(_timeProvider.GetUtcNow().UtcDateTime);
        switch (blocker)
        {
            case NotificationResendBlocker.Final:
                return Result<NotificationDetailDto>.Failure(NotificationErrors.Final(log.Id, log.Status.ToString()));

            case NotificationResendBlocker.AttemptInProgress:
                return Result<NotificationDetailDto>.Failure(NotificationErrors.AttemptInProgress(log.Id));

            case NotificationResendBlocker.NoPayload:
                return Result<NotificationDetailDto>.Failure(new Error(
                    NotificationErrors.NotResendableCode,
                    $"Notification {log.Id} kept no copy of its event, so there is nothing to send again. A password "
                    + "reset is never kept (its link carries a live token); ask the customer to request a new one."));
        }

        if (!_redispatcher.CanRedispatch(log.EventType))
        {
            return Result<NotificationDetailDto>.Failure(new Error(
                NotificationErrors.NotResendableCode,
                $"Notification {log.Id} is a {log.EventType}, which no consumer in this service delivers any more."));
        }

        if (!_redispatcher.IsAvailable)
        {
            return Result<NotificationDetailDto>.Failure(new Error(
                NotificationErrors.BusUnavailableCode,
                "This Notification host runs no message bus, so a resend has nowhere to go."));
        }

        try
        {
            await _redispatcher.RedispatchAsync(log.EventType, log.Payload!, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The resend of notification {NotificationId} could not be queued.", log.Id);
            return Result<NotificationDetailDto>.Failure(new Error(
                NotificationErrors.DispatchFailedCode,
                "The message bus refused the resend. The reason is in the service log."));
        }

        _logger.LogInformation(
            "Operator {ActorId} queued a resend of notification {NotificationId} (EventId={EventId}, {EventType}).",
            _currentUser.UserId, log.Id, log.EventId, log.EventType);

        return Result<NotificationDetailDto>.Success(log.ToDetailDto());
    }
}
