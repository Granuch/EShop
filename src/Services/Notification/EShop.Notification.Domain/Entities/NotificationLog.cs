namespace EShop.Notification.Domain.Entities;

/// <summary>
/// One notification for one integration event, and the durable record of its delivery (Notification audit D1).
/// <para>Since Notification audit S2 (D5) the row, unique on <see cref="EventId"/>, is also the claim that stops one
/// event being emailed twice. A delivery moves it Pending → Sending → Sent, or Sending → Failed, from which a retry
/// starts a new attempt. <see cref="Version"/> (the row version) stops two deliveries that read the same state from both
/// starting an attempt.</para>
/// </summary>
public sealed class NotificationLog
{
    /// <summary>
    /// How long an attempt holds its claim (Notification audit D5). Longer than an attempt can take — the Identity lookup
    /// is bounded to about 18 s and MailKit allows 2 minutes per SMTP operation — so a live attempt is never taken over;
    /// shorter than the 15-minute delayed redelivery, so a message whose attempt died with its process is delivered by a
    /// later redelivery instead of ending in the error queue.
    /// <para>Here rather than beside the consumers since Admin panel S13: the operator's actions (resend,
    /// mark-undeliverable) must refuse a live attempt by the same rule the delivery path takes it over by, and Application
    /// cannot reach Infrastructure. <c>NotificationDelivery.AttemptLease</c> still names it for the consumers.</para>
    /// </summary>
    public static readonly TimeSpan AttemptLease = TimeSpan.FromMinutes(5);

    private NotificationLog()
    {
    }

    private NotificationLog(
        Guid eventId,
        string eventType,
        string? correlationId,
        string? userId,
        string templateName,
        string subject,
        string? payload)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        CorrelationId = correlationId;
        UserId = userId;
        TemplateName = templateName;
        Subject = subject;
        Payload = payload;
        Status = NotificationStatus.Pending;
        RetryCount = 0;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public string? UserId { get; private set; }

    /// <summary>The address the email went, or was going, to. Null until the recipient is known.</summary>
    public string? RecipientEmail { get; private set; }

    public string TemplateName { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public NotificationStatus Status { get; private set; }

    /// <summary>How many attempts have failed.</summary>
    public int RetryCount { get; private set; }

    public string? LastError { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>When the latest attempt started. With <see cref="NotificationStatus.Sending"/>, it dates the claim.</summary>
    public DateTime? AttemptStartedAt { get; private set; }

    /// <summary>The row version (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    /// <summary>
    /// The integration event this notification was built from, as JSON — what an operator's resend sends again (Admin
    /// panel S13). Nothing else on the row can rebuild the email: the consumers render it from the live event.
    /// <para><b>Null on purpose</b> for an <c>ISensitivePayloadEvent</c> (the password reset): its payload is a live reset
    /// token, and keeping it here would put that token at rest for the whole 90-day retention window — the SEC-05
    /// exposure the outbox redaction exists to prevent. Null also for every row written before the column existed. Either
    /// way the notification cannot be resent, and says so.</para>
    /// </summary>
    public string? Payload { get; private set; }

    public static NotificationLog CreatePending(
        Guid eventId,
        string eventType,
        string? correlationId,
        string? userId,
        string templateName,
        string subject,
        string? payload = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type is required.", nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new ArgumentException("Template name is required.", nameof(templateName));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        return new NotificationLog(
            eventId,
            eventType,
            correlationId,
            userId,
            templateName,
            subject,
            string.IsNullOrWhiteSpace(payload) ? null : payload);
    }

    /// <summary>
    /// Whether another delivery holds a live attempt: the row is <see cref="NotificationStatus.Sending"/> and the attempt
    /// started less than <paramref name="lease"/> ago. An older attempt is taken to have died with its process.
    /// </summary>
    public bool IsAttemptInProgress(DateTime now, TimeSpan lease)
        => Status == NotificationStatus.Sending
           && AttemptStartedAt is { } started
           && now - started < lease;

    /// <summary>Sent or undeliverable: nothing will ever be attempted again.</summary>
    public bool IsFinal => Status is NotificationStatus.Sent or NotificationStatus.Undeliverable;

    /// <summary>Claims the notification for one delivery attempt. A final notification is never attempted again.</summary>
    public void BeginAttempt(DateTime now)
    {
        if (IsFinal)
        {
            throw new InvalidOperationException($"The notification for event {EventId} is {Status}; it is not attempted again.");
        }

        Status = NotificationStatus.Sending;
        AttemptStartedAt = now;
        UpdatedAt = now;
    }

    public void RecordRecipient(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Recipient email is required.", nameof(email));
        }

        RecipientEmail = email;
    }

    public void MarkSent(string? providerMessageId)
    {
        if (Status != NotificationStatus.Sending)
        {
            throw new InvalidOperationException(
                $"The notification for event {EventId} is {Status}; only an attempt in progress can be marked sent.");
        }

        Status = NotificationStatus.Sent;
        ProviderMessageId = providerMessageId;
        LastError = null;
        SentAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Ends the current attempt as failed, and counts it.</summary>
    public void MarkFailed(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Failure reason is required.", nameof(error));
        }

        if (IsFinal)
        {
            throw new InvalidOperationException($"The notification for event {EventId} is {Status}; it cannot fail again.");
        }

        Status = NotificationStatus.Failed;
        RetryCount++;
        LastError = SanitizeError(error);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Ends the attempt for good (Notification audit D3): the recipient cannot exist or has no address, so no retry can
    /// help. Not counted as a failed attempt. The reason is EShop's own wording and is stored as it is;
    /// <see cref="SanitizeError"/> is for provider exception text.
    /// </summary>
    public void MarkUndeliverable(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        if (Status != NotificationStatus.Sending)
        {
            throw new InvalidOperationException(
                $"The notification for event {EventId} is {Status}; only an attempt in progress can end undeliverable.");
        }

        Status = NotificationStatus.Undeliverable;
        LastError = reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Why an operator's resend would be refused at <paramref name="now"/>, or null when it may go ahead (Admin panel
    /// S13). In the order an operator needs to hear them: a final notification is never attempted again, a live attempt
    /// must be left to finish, and a notification whose event was not kept has nothing to resend.
    /// </summary>
    public NotificationResendBlocker? ResendBlockerAt(DateTime now)
    {
        if (IsFinal)
        {
            return NotificationResendBlocker.Final;
        }

        if (IsAttemptInProgress(now, AttemptLease))
        {
            return NotificationResendBlocker.AttemptInProgress;
        }

        return Payload is null ? NotificationResendBlocker.NoPayload : null;
    }

    /// <summary>
    /// An operator ends the notification for good (Admin panel S13, risk A5) — say, an address that bounces for a reason
    /// no retry will cure, which only a person reading the mail server's logs can know.
    /// <para>
    /// <b>Separate from <see cref="MarkUndeliverable"/>, which is unchanged.</b> That one is the delivery path's and
    /// requires <see cref="NotificationStatus.Sending"/>, correctly: the attempt it ends is its own. An operator acts from
    /// outside any attempt, so this one is permitted from <see cref="NotificationStatus.Pending"/>,
    /// <see cref="NotificationStatus.Failed"/>, or a <see cref="NotificationStatus.Sending"/> whose lease has expired.
    /// </para>
    /// <para>
    /// <b>A live attempt is refused, which narrows plan §4.4</b> ("from Failed or Sending"). An attempt holding its lease
    /// may be past its SMTP send: this write would bump the row version, the attempt's <c>MarkSent</c> save would then
    /// fail its concurrency check, and the delivery path deliberately swallows that failure (D2) — leaving a row that says
    /// Undeliverable for an email the customer has. Waiting at most one lease is the cheaper wrong.
    /// </para>
    /// Not counted in <see cref="RetryCount"/>, like the delivery path's undeliverable outcome.
    /// </summary>
    public void MarkUndeliverableByOperator(string reason, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }

        if (IsFinal)
        {
            throw new InvalidOperationException(
                $"The notification for event {EventId} is {Status}; it cannot be marked undeliverable.");
        }

        if (IsAttemptInProgress(now, AttemptLease))
        {
            throw new InvalidOperationException(
                $"An attempt to deliver the notification for event {EventId} is in progress; it cannot be marked undeliverable until it ends.");
        }

        Status = NotificationStatus.Undeliverable;
        LastError = $"{OperatorReasonPrefix}{reason.Trim()}";
        UpdatedAt = now;
    }

    /// <summary>
    /// Prefixed to an operator's reason, so the journal tells "an operator decided" from "the delivery path found no such
    /// recipient" — both end <see cref="NotificationStatus.Undeliverable"/> with a reason in <see cref="LastError"/>.
    /// </summary>
    public const string OperatorReasonPrefix = "Marked undeliverable by an operator: ";

    private static string SanitizeError(string rawError)
    {
        var error = rawError.Trim();

        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || error.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "Email provider timeout.";
        }

        if (error.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || error.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || error.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        {
            return "Email provider authentication failed.";
        }

        if (error.Contains("smtp", StringComparison.OrdinalIgnoreCase)
            || error.Contains("socket", StringComparison.OrdinalIgnoreCase)
            || error.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || error.Contains("network", StringComparison.OrdinalIgnoreCase)
            || error.Contains("host", StringComparison.OrdinalIgnoreCase))
        {
            return "Email provider connectivity error.";
        }

        if (error.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || error.Contains("recipient", StringComparison.OrdinalIgnoreCase)
            || error.Contains("address", StringComparison.OrdinalIgnoreCase)
            || error.Contains("mailbox", StringComparison.OrdinalIgnoreCase))
        {
            return "Email request validation failed.";
        }

        return "Email sending failed.";
    }
}

public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,

    /// <summary>An attempt holds the claim (Notification audit D5).</summary>
    Sending = 3,

    /// <summary>Final: the recipient cannot exist or has no address, so no retry can help (Notification audit D3).</summary>
    Undeliverable = 4
}

/// <summary>Why a notification cannot be resent right now (Admin panel S13).</summary>
public enum NotificationResendBlocker
{
    /// <summary>Sent or undeliverable: nothing will ever be attempted again.</summary>
    Final,

    /// <summary>Another attempt holds a live lease; resending now could only race it.</summary>
    AttemptInProgress,

    /// <summary>The event was not kept — a password reset, or a row from before the payload column existed.</summary>
    NoPayload
}
