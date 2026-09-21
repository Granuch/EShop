using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Resend;
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
///   <item>A recipient that cannot exist — Identity says 404 or 400, has no usable address, or the event has no
///   UserId — ends the notification as <see cref="NotificationStatus.Undeliverable"/>, and the message is acknowledged
///   (S3, D3): a permanent failure no longer spends 16 attempts and trips the circuit breaker. A failure a later attempt
///   may cure (Identity or SMTP down, or Identity refusing the API key, which is logged as an error) is recorded
///   (<c>Failed</c>, counted, reason kept) and rethrown for retry.</item>
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

    /// <returns>The sent message's Message-ID, recorded as the notification's provider message id (S7, L21).</returns>
    protected abstract Task<string> SendAsync(TEvent message, RecipientAddress recipient, CancellationToken cancellationToken);

    /// <summary>
    /// The name an email greets the recipient by: Identity's display name, else "there" ("Hi there,"). It used to fall back
    /// to the user id, so a customer without a name was greeted with a GUID (Notification audit S7, L16).
    /// </summary>
    protected static string GreetingName(RecipientAddress recipient) => recipient.DisplayName ?? "there";

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
            await DeliverAsync(message, correlationId, context.GetRetryAttempt(), context.CancellationToken);
        }
    }

    private async Task DeliverAsync(
        TEvent message,
        string? correlationId,
        int retryAttempt,
        CancellationToken cancellationToken)
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
                // A publisher defect; retrying cannot supply the id (M2, D3).
                Logger.LogError(
                    "{EventType} {EventId} carries no UserId. The notification cannot be delivered and is not retried.",
                    typeof(TEvent).Name, message.EventId);
                await RecordUndeliverableAsync(log, "UserId is missing from the event.");
                return;
            }

            RecipientLookup lookup;
            try
            {
                lookup = await _userContactResolver.ResolveAsync(userId, cancellationToken);
            }
            catch (UserContactUnavailableException ex) when (ex.IsConfigurationError)
            {
                Logger.LogError(ex,
                    "Identity refused the contact lookup for EventId={EventId}. Every notification fails until "
                    + "IdentityService:ApiKey is fixed; the message is retried (retry attempt {RetryAttempt}).",
                    message.EventId, retryAttempt);
                await RecordFailureAsync(log, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "The recipient of EventId={EventId} could not be looked up (retry attempt {RetryAttempt}).",
                    message.EventId, retryAttempt);
                await RecordFailureAsync(log, ReasonOf(ex));
                throw;
            }

            if (lookup.Recipient is null)
            {
                Logger.LogWarning(
                    "The notification for EventId={EventId} cannot be delivered and is not retried: {Reason}",
                    message.EventId, lookup.UndeliverableReason);
                await RecordUndeliverableAsync(log, lookup.UndeliverableReason!);
                return;
            }

            recipient = lookup.Recipient;
        }

        log.RecordRecipient(recipient.Email);

        string providerMessageId;
        try
        {
            providerMessageId = await SendAsync(message, recipient, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "The {TemplateName} email for EventId={EventId} could not be sent (retry attempt {RetryAttempt}).",
                TemplateName, message.EventId, retryAttempt);
            await RecordFailureAsync(log, ReasonOf(ex));
            throw;
        }

        // D2: the email is out. Nothing below may throw, or the redelivery would send it a second time.
        try
        {
            log.MarkSent(providerMessageId);
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
            // Admin panel S13: the event is kept on the row (null for a password reset), because it is the only thing an
            // operator's resend can rebuild the email from. Only on insert — a redelivery or a resend finds the row.
            var pending = NotificationLog.CreatePending(
                message.EventId,
                typeof(TEvent).Name,
                correlationId,
                UserIdOf(message),
                TemplateName,
                SubjectFor(message),
                NotificationPayload.Serialize(message));

            log = await _logs.TryAddAsync(pending, cancellationToken)
                ? pending
                : await _logs.FindByEventIdAsync(message.EventId, cancellationToken)
                  ?? throw new InvalidOperationException(
                      $"The NotificationLog for event {message.EventId} was neither inserted nor found.");
        }

        if (log.IsFinal)
        {
            Logger.LogInformation(
                "The notification for EventId={EventId} is already {Status}. Skipping.", message.EventId, log.Status);
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

    /// <summary>Ends the notification for good (D3), in its own commit, and acknowledges the message.</summary>
    private async Task RecordUndeliverableAsync(NotificationLog log, string reason)
    {
        try
        {
            log.MarkUndeliverable(reason);
            await _logs.SaveAsync(log, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "The undeliverable outcome of EventId={EventId} could not be recorded.", log.EventId);
        }
    }

    private static string ReasonOf(Exception exception)
        => string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
}

public static class NotificationDelivery
{
    /// <summary>
    /// How long an attempt holds its claim (Notification audit D5) — <see cref="NotificationLog.AttemptLease"/>, where the
    /// reasoning lives. Defined there since Admin panel S13 so the operator's actions refuse a live attempt by the same
    /// rule this delivery path takes a dead one over by; two constants would drift.
    /// </summary>
    public static readonly TimeSpan AttemptLease = NotificationLog.AttemptLease;
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
