using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PaymentFailedConsumer : NotificationConsumer<PaymentFailedEvent>
{
    private readonly IEmailService _emailService;
    private readonly string _supportEmail;

    public PaymentFailedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        IOptions<SmtpSettings> smtpSettings,
        TimeProvider timeProvider,
        ILogger<PaymentFailedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
        // Smtp:FromEmail is checked once, at startup, by NotificationConfigurationGuard (Notification audit S4, M10).
        _supportEmail = smtpSettings.Value.FromEmail;
    }

    protected override string TemplateName => "payment-failed";

    // Notification audit S7 (M12, D10): Ordering cancels a pending order when its payment fails, so the email says so.
    protected override string SubjectFor(PaymentFailedEvent message) => $"Your order #{message.OrderId} was cancelled";

    protected override string? UserIdOf(PaymentFailedEvent message) => message.UserId;

    protected override Task<string> SendAsync(PaymentFailedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendPaymentFailedAsync(
            recipient,
            new PaymentFailedEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = GreetingName(recipient),
                FailureReason = message.Reason,
                SupportEmail = _supportEmail
            },
            cancellationToken);
}
