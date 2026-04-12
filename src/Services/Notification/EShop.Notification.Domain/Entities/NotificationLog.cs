namespace EShop.Notification.Domain.Entities;

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
        string recipientEmail,
        string templateName,
        string subject)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        CorrelationId = correlationId;
        UserId = userId;
        RecipientEmail = recipientEmail;
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
    public string RecipientEmail { get; private set; } = string.Empty;
    public string TemplateName { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public NotificationStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public static NotificationLog CreatePending(
        Guid eventId,
        string eventType,
        string? correlationId,
        string? userId,
        string recipientEmail,
        string templateName,
        string subject)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type is required.", nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            throw new ArgumentException("Recipient email is required.", nameof(recipientEmail));
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
            recipientEmail,
            templateName,
            subject);
    }

    public void MarkSent(string? providerMessageId)
    {
        Status = NotificationStatus.Sent;
        ProviderMessageId = providerMessageId;
        LastError = null;
        SentAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Failure reason is required.", nameof(error));
        }

        Status = NotificationStatus.Failed;
        LastError = SanitizeError(error);
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncrementRetry()
    {
        RetryCount++;
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
    Failed = 2
}
