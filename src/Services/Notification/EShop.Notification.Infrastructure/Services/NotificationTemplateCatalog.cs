using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Resend;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Services;

/// <inheritdoc cref="INotificationTemplateCatalog"/>
/// <remarks>
/// <para>
/// <b>A test send goes through <see cref="IEmailService"/>, not a separate renderer call</b>, so what an operator
/// receives is byte-for-byte what a customer would: the same template, the same token encoding, the same subject line,
/// the same plain-text part and the same SMTP settings. That is the whole value of the button — a test that took a
/// shortcut would pass on a template the real path breaks.
/// </para>
/// <para>
/// The subject is therefore the real one, with no "[TEST]" prefix; the sample data is what marks it (an all-zero order
/// id, and a greeting to whoever the operator named).
/// </para>
/// </remarks>
public sealed class NotificationTemplateCatalog : INotificationTemplateCatalog
{
    /// <summary>The order id every sample carries: unmistakably not a real order.</summary>
    public static readonly Guid SampleOrderId = Guid.Empty;

    private static readonly DateTime SampleDate = new(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<NotificationTemplateInfo> All =
    [
        Info(NotificationTemplates.OrderCreated, nameof(OrderCreatedEvent)),
        Info(NotificationTemplates.OrderShipped, nameof(OrderShippedEvent)),
        Info(NotificationTemplates.PaymentCreated, nameof(PaymentCreatedEvent)),
        Info(NotificationTemplates.PaymentCompleted, nameof(PaymentCompletedEvent)),
        Info(NotificationTemplates.PaymentFailed, nameof(PaymentFailedEvent)),
        Info(NotificationTemplates.PaymentRefunded, nameof(PaymentRefundedEvent)),
        Info(NotificationTemplates.PasswordReset, nameof(PasswordResetRequestedIntegrationEvent))
    ];

    private readonly IEmailService _emailService;
    private readonly SmtpSettings _smtpSettings;
    private readonly PasswordResetSettings _passwordResetSettings;

    public NotificationTemplateCatalog(
        IEmailService emailService,
        IOptions<SmtpSettings> smtpSettings,
        IOptions<PasswordResetSettings> passwordResetSettings)
    {
        _emailService = emailService;
        _smtpSettings = smtpSettings.Value;
        _passwordResetSettings = passwordResetSettings.Value;
    }

    public IReadOnlyList<NotificationTemplateInfo> Templates => All;

    public Task<string> SendTestAsync(
        string templateName,
        RecipientAddress recipient,
        CancellationToken cancellationToken = default)
    {
        var name = recipient.DisplayName ?? "there";

        return templateName switch
        {
            NotificationTemplates.OrderCreated => _emailService.SendOrderConfirmationAsync(recipient, new OrderConfirmationEmailModel
            {
                OrderId = SampleOrderId,
                CustomerName = name,
                OrderDate = SampleDate,
                TotalAmount = 123.45m,
                Currency = "USD",
                ItemCount = 3
            }, cancellationToken),

            NotificationTemplates.OrderShipped => _emailService.SendOrderShippedAsync(recipient, new OrderShippedEmailModel
            {
                OrderId = SampleOrderId,
                CustomerName = name,
                TrackingNumber = "TEST-TRACKING-0000",
                ShippedAt = SampleDate
            }, cancellationToken),

            NotificationTemplates.PaymentCreated => _emailService.SendPaymentCreatedAsync(recipient, new PaymentCreatedEmailModel
            {
                OrderId = SampleOrderId,
                CustomerName = name,
                Amount = 123.45m,
                Currency = "USD",
                CreatedAt = SampleDate
            }, cancellationToken),

            NotificationTemplates.PaymentCompleted => _emailService.SendPaymentCompletedAsync(recipient, new PaymentCompletedEmailModel
            {
                OrderId = SampleOrderId,
                CustomerName = name,
                Amount = 123.45m,
                Currency = "USD",
                CompletedAt = SampleDate
            }, cancellationToken),

            NotificationTemplates.PaymentFailed => _emailService.SendPaymentFailedAsync(recipient, new PaymentFailedEmailModel
            {
                OrderId = SampleOrderId,
                CustomerName = name,
                FailureReason = "Sample failure reason (test send).",
                SupportEmail = _smtpSettings.FromEmail
            }, cancellationToken),

            NotificationTemplates.PaymentRefunded => _emailService.SendPaymentRefundedAsync(recipient, new PaymentRefundedEmailModel
            {
                OrderId = SampleOrderId,
                CustomerName = name,
                Amount = 123.45m,
                Currency = "USD",
                RefundedAt = SampleDate,
                SupportEmail = _smtpSettings.FromEmail
            }, cancellationToken),

            // A link to the real reset page with a token no account can hold: the operator sees the page the customer
            // lands on, and nothing is reset if the link is followed.
            NotificationTemplates.PasswordReset => _emailService.SendPasswordResetAsync(recipient, new PasswordResetEmailModel
            {
                CustomerName = name,
                ResetLink = SampleResetLink()
            }, cancellationToken),

            _ => throw new ArgumentException($"There is no template named '{templateName}'.", nameof(templateName))
        };
    }

    private string SampleResetLink()
    {
        var baseUrl = _passwordResetSettings.ResetUrlBase;
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}userId=test-send&token=not-a-real-token";
    }

    private static NotificationTemplateInfo Info(string name, string eventType)
        => new(name, eventType, ResendableNotifications.TryGet(eventType, out _));
}
