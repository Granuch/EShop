using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.ValueObjects;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Consumers;

/// <summary>
/// The one delivery flow every Notification consumer runs (Notification audit S2). A consumer supplies only its
/// template, subject, user id and the send itself.
///
/// <para><b>Why not <c>IdempotentConsumer</c>.</b> That base runs the handler inside one transaction with its
/// <c>processed_messages</c> claim and rolls everything back on a throw. For email that was wrong twice over: a failure
/// written before the throw was rolled back with it, so the log only ever held <c>Sent</c> rows (H1), and an email
/// already sent was sent again when anything after the send failed (H3). It also held a database transaction open across
/// the Identity call and the SMTP send (M4).</para>
///
/// <para><b>The flow</b> (D1, D2, D5). Every step is its own commit; nothing spans the external calls.</para>
/// <list type="number">
///   <item>The <see cref="NotificationLog"/> row, unique on <c>EventId</c>, is the claim. A delivery inserts it if it is
///   missing, then moves it to <see cref="NotificationStatus.Sending"/> and commits. The row version stops two deliveries
///   that read the same state from both doing so; the loser's <c>DbUpdateConcurrencyException</c> is retried.</item>
///   <item>A <c>Sent</c> row means a duplicate: acknowledged, nothing sent. A <c>Sending</c> row younger than
///   <see cref="NotificationDelivery.AttemptLease"/> means another delivery is sending right now:
///   <see cref="NotificationDeliveryInProgressException"/>, retried later. An older one is taken over — its process is
///   assumed dead, and if it died after its send, this is the one duplicate D2 accepts.</item>
///   <item>A failure to resolve or send is recorded (<c>Failed</c>, counted, reason kept) and rethrown for retry.</item>
///   <item>Once the send has succeeded nothing rethrows: a failure to record <c>Sent</c> is logged, because rethrowing
///   would redeliver an email the customer already has.</item>
/// </list>
/// </summary>
public abstract class NotificationConsumer<TEvent> : IConsumer<TEvent>
    where TEvent : IntegrationEvent
{
    private readonly INotificationLogRepository _logs;
    private readonly IUserContactResolver _userContactResolver;
    private readonly TimeProvider _timeProvider;

    protected NotificationConsumer(
        INotificationLogRepository logs,
        IUserContactResolver userContactResolver,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _logs = logs;
        _userContactResolver = userContactResolver;
        _timeProvider = timeProvider;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    protected abstract string TemplateName { get; }

    protected abstract string SubjectFor(TEvent message);

    protected abstract string? UserIdOf(TEvent message);

    /// <summary>A recipient the event carries itself, so no Identity lookup is needed. None by default.</summary>
    protected virtual RecipientAddress? RecipientFromEvent(TEvent message) => null;

    protected abstract Task SendAsync(TEvent message, RecipientAddress recipient, CancellationToken cancellationToken);

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var message = context.Message;
        if (message.EventId == Guid.Empty)
        {
            // An ArgumentException is not retried: without an EventId there is nothing to claim.
            throw new ArgumentException($"{typeof(TEvent).Name} has no EventId, so its delivery cannot be claimed.");
        }

        // M3: the payload's own id first, as IdempotentConsumer does. The transport header is at best a reformatted
        // copy of it and at worst unrelated.
        var correlationId = !string.IsNullOrWhiteSpace(message.CorrelationId)
            ? message.CorrelationId
            : context.CorrelationId?.ToString();

        using (Logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = message.EventId,
            ["CorrelationId"] = correlationId,
            ["MessageType"] = typeof(TEvent).Name
        }))
        using (AmbientCorrelation.Begin(correlationId))
        {
            await DeliverAsync(message, correlationId, context.CancellationToken);
        }
    }

    private async Task DeliverAsync(TEvent message, string? correlationId, CancellationToken cancellationToken)
    {
        var log = await ClaimAsync(message, correlationId, cancellationToken);
        if (log is null)
        {
            return;
        }

        var recipient = RecipientFromEvent(message);
        if (recipient is null)
        {
            var userId = UserIdOf(message);
            if (string.IsNullOrWhiteSpace(userId))
            {
                // Retrying cannot help, so the message is acknowledged (it was PaymentFailedConsumer's rule alone; M2).
                Logger.LogError(
                    "{EventType} {EventId} carries no UserId. The notification cannot be delivered and is not retried.",
                    typeof(TEvent).Name, message.EventId);
                await RecordFailureAsync(log, "UserId is missing from the event; the notification cannot be delivered.");
                return;
            }

            try
            {
                recipient = await _userContactResolver.ResolveAsync(userId, cancellationToken);
            }
            catch (Exception ex)
            {
                await RecordFailureAsync(log, ReasonOf(ex));
                throw;
            }

            if (recipient is null)
            {
                Logger.LogWarning("Recipient email resolution failed for UserId={UserId}", userId);
                await RecordFailureAsync(log, "Recipient email could not be resolved.");
                throw new InvalidOperationException("Recipient email could not be resolved.");
            }
        }

        log.RecordRecipient(recipient.Email);

        try
        {
            await SendAsync(message, recipient, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "The {TemplateName} email for EventId={EventId} could not be sent.", TemplateName, message.EventId);
            await RecordFailureAsync(log, ReasonOf(ex));
            throw;
        }

        // D2: the email is out. Nothing below may throw, or the redelivery would send it a second time.
        try
        {
            log.MarkSent(providerMessageId: null);
            await _logs.SaveAsync(log, CancellationToken.None);
            Logger.LogInformation("The {TemplateName} email for EventId={EventId} was sent.", TemplateName, message.EventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "The {TemplateName} email for EventId={EventId} was sent, but its Sent state could not be recorded. "
                + "The message is acknowledged so the email is not sent twice.",
                TemplateName, message.EventId);
        }
    }

    /// <summary>The log row, claimed for one attempt and committed; null when the notification was already sent.</summary>
    private async Task<NotificationLog?> ClaimAsync(TEvent message, string? correlationId, CancellationToken cancellationToken)
    {
        var log = await _logs.FindByEventIdAsync(message.EventId, cancellationToken);
        if (log is null)
        {
            var pending = NotificationLog.CreatePending(
                message.EventId,
                typeof(TEvent).Name,
                correlationId,
                UserIdOf(message),
                TemplateName,
                SubjectFor(message));

            log = await _logs.TryAddAsync(pending, cancellationToken)
                ? pending
                : await _logs.FindByEventIdAsync(message.EventId, cancellationToken)
                  ?? throw new InvalidOperationException(
                      $"The NotificationLog for event {message.EventId} was neither inserted nor found.");
        }

        if (log.Status == NotificationStatus.Sent)
        {
            Logger.LogInformation("The notification for EventId={EventId} was already sent. Skipping.", message.EventId);
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (log.IsAttemptInProgress(now, NotificationDelivery.AttemptLease))
        {
            throw new NotificationDeliveryInProgressException(message.EventId);
        }

        log.BeginAttempt(now);
        await _logs.SaveAsync(log, cancellationToken);
        return log;
    }

    /// <summary>
    /// Records a failed attempt in its own commit (D1). Saved without the message's token, which may be the reason the
    /// attempt failed; a failure to save is logged so that it cannot hide the original exception (L3).
    /// </summary>
    private async Task RecordFailureAsync(NotificationLog log, string reason)
    {
        try
        {
            log.MarkFailed(reason);
            await _logs.SaveAsync(log, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "The failure of the notification for EventId={EventId} could not be recorded.", log.EventId);
        }
    }

    private static string ReasonOf(Exception exception)
        => string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
}

public static class NotificationDelivery
{
    /// <summary>
    /// How long an attempt holds its claim (Notification audit D5). Longer than an attempt can take — the Identity lookup
    /// is bounded to about 18 s and MailKit allows 2 minutes per SMTP operation — so a live attempt is never taken over;
    /// shorter than the 15-minute delayed redelivery, so a message whose attempt died with its process is delivered by a
    /// later redelivery instead of ending in the error queue.
    /// </summary>
    public static readonly TimeSpan AttemptLease = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Another delivery of the same event holds a live attempt. Thrown so MassTransit retries later, by which time that
/// attempt has recorded its outcome or its lease has expired (Notification audit D5).
/// </summary>
public sealed class NotificationDeliveryInProgressException(Guid eventId)
    : Exception($"A delivery of event {eventId} is already in progress.")
{
    public Guid EventId { get; } = eventId;
}
