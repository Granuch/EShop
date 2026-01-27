namespace EShop.Notification.Domain.Entities;

/// <summary>
/// Notification log entity
/// </summary>
public class NotificationLog
{
    public Guid Id { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public NotificationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    // TODO: Add template name/ID
    // TODO: Add support for SMS notifications
}

public enum NotificationType
{
    OrderConfirmation,
    PaymentSuccess,
    PaymentFailed,
    OrderShipped,
    OrderDelivered,
    PasswordReset,
    WelcomeEmail
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed
}
