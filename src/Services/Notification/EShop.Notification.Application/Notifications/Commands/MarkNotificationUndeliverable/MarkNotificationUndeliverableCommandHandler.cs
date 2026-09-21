using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Application.Notifications.Commands.MarkNotificationUndeliverable;

/// <summary>
/// <para>
/// <b>Through the repository, not the query service</b>: this is a write, so it needs the tracked row and its
/// <c>xmin</c> row version. A delivery that claims the row between this read and this save makes the save match no
/// row; the <c>DbUpdateConcurrencyException</c> propagates and <c>AddEfConcurrency()</c> answers 409. That is the right
/// outcome — the operator decided on a state that no longer exists — and it is why the exception is not caught here.
/// </para>
/// <para>
/// No <c>ITransactionalCommand</c>: this service has no <c>TransactionBehavior</c>, and a single-row save is atomic on
/// its own. Every write here is its own commit, as the delivery path's are (Notification audit D1).
/// </para>
/// </summary>
public sealed class MarkNotificationUndeliverableCommandHandler
    : IRequestHandler<MarkNotificationUndeliverableCommand, Result<NotificationDetailDto>>
{
    private readonly INotificationLogRepository _logs;
    private readonly ICurrentUserContext _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkNotificationUndeliverableCommandHandler> _logger;

    public MarkNotificationUndeliverableCommandHandler(
        INotificationLogRepository logs,
        ICurrentUserContext currentUser,
        TimeProvider timeProvider,
        ILogger<MarkNotificationUndeliverableCommandHandler> logger)
    {
        _logs = logs;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<NotificationDetailDto>> Handle(
        MarkNotificationUndeliverableCommand request,
        CancellationToken cancellationToken)
    {
        var log = await _logs.FindByIdAsync(request.Id, cancellationToken);
        if (log is null)
        {
            return Result<NotificationDetailDto>.Failure(NotificationErrors.NotFound(request.Id));
        }

        // Checked here so the operator gets a 409 naming the reason; the domain method refuses the same two states
        // with an exception, as the backstop for any caller that skips this.
        if (log.IsFinal)
        {
            return Result<NotificationDetailDto>.Failure(NotificationErrors.Final(log.Id, log.Status.ToString()));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (log.IsAttemptInProgress(now, NotificationLog.AttemptLease))
        {
            return Result<NotificationDetailDto>.Failure(NotificationErrors.AttemptInProgress(log.Id));
        }

        log.MarkUndeliverableByOperator(request.Reason!, now);
        await _logs.SaveAsync(log, cancellationToken);

        _logger.LogInformation(
            "Operator {ActorId} marked notification {NotificationId} (EventId={EventId}) undeliverable.",
            _currentUser.UserId, log.Id, log.EventId);

        return Result<NotificationDetailDto>.Success(log.ToDetailDto());
    }
}
