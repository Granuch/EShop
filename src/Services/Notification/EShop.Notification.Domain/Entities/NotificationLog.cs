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
    private NotificationLog()
    {
    }

    private NotificationLog(
        Guid eventId,
        string eventType,
        string? correlationId,
        string? userId,
        string templateName,
        string subject)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        CorrelationId = correlationId;
        UserId = userId;
        TemplateName = templateName;
        Subject = subject;
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

    public static NotificationLog CreatePending(
        Guid eventId,
        string eventType,
        string? correlationId,
        string? userId,
        string templateName,
        string subject)
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
            subject);
    }

    /// <summary>
    /// Whether another delivery holds a live attempt: the row is <see cref="NotificationStatus.Sending"/> and the attempt
    /// started less than <paramref name="lease"/> ago. An older attempt is taken to have died with its process.
    /// </summary>
    public bool IsAttemptInProgress(DateTime now, TimeSpan lease)
        => Status == NotificationStatus.Sending
           && AttemptStartedAt is { } started
           && now - started < lease;

    /// <summary>Claims the notification for one delivery attempt. A sent notification is never attempted again.</summary>
    public void BeginAttempt(DateTime now)
    {
        if (Status == NotificationStatus.Sent)
        {
            throw new InvalidOperationException($"The notification for event {EventId} was already sent.");
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

        if (Status == NotificationStatus.Sent)
        {
            throw new InvalidOperationException($"The notification for event {EventId} was already sent.");
        }

        Status = NotificationStatus.Failed;
        RetryCount++;
        LastError = SanitizeError(error);
        UpdatedAt = DateTime.UtcNow;
    }

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
    Sending = 3
}
