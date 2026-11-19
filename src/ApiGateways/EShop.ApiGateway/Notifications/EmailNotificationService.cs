using EShop.ApiGateway.Telemetry;

namespace EShop.ApiGateway.Notifications;

/// <summary>
/// Puts a notice on the dispatch queue unless <see cref="GatewayNoticeThrottle"/> holds it back. A sent notice carries
/// the number of its kind held back since the previous one, so the email still says how big the burst was.
/// </summary>
public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly GatewayEmailQueue _queue;
    private readonly GatewayNoticeThrottle _throttle;

    public EmailNotificationService(GatewayEmailQueue queue, GatewayNoticeThrottle throttle)
    {
        _queue = queue;
        _throttle = throttle;
    }

    public Task QueueAsync(EmailNotificationContext context, CancellationToken cancellationToken = default)
    {
        var decision = _throttle.Evaluate(context);
        if (!decision.IsSent)
        {
            GatewayTelemetry.RecordEmailSuppressed(context.EventType, decision.Reason!);
            return Task.CompletedTask;
        }

        GatewayTelemetry.RecordEmailQueued(context.EventType);
        return _queue.EnqueueAsync(context with { SuppressedCount = decision.SuppressedBefore }, cancellationToken).AsTask();
    }
}
