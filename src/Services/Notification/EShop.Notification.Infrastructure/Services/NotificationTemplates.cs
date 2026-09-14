namespace EShop.Notification.Infrastructure.Services;

/// <summary>
/// The email templates (<c>Templates/*.html</c>) by name, as <see cref="EmailService"/> renders them. Notification audit
/// S4 (L23): the startup guard checks that every one exists, so a template missing from a build stops the deploy instead
/// of failing every message of its type.
/// </summary>
public static class NotificationTemplates
{
    public const string OrderCreated = "order-created";
    public const string OrderShipped = "order-shipped";
    public const string PaymentCreated = "payment-created";
    public const string PaymentCompleted = "payment-completed";
    public const string PaymentFailed = "payment-failed";
    public const string PaymentRefunded = "payment-refunded";
    public const string PasswordReset = "password-reset";

    public static IReadOnlyList<string> All { get; } =
    [
        OrderCreated, OrderShipped, PaymentCreated, PaymentCompleted, PaymentFailed, PaymentRefunded, PasswordReset
    ];
}
