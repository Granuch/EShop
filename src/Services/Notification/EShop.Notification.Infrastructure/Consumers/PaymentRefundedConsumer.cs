using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PaymentRefundedConsumer : NotificationConsumer<PaymentRefundedEvent>
{
    private readonly IEmailService _emailService;
    private readonly string _supportEmail;

    public PaymentRefundedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        IOptions<SmtpSettings> smtpSettings,
        TimeProvider timeProvider,
        ILogger<PaymentRefundedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
        // Smtp:FromEmail is checked once, at startup, by NotificationConfigurationGuard (Notification audit S4, M10).
        _supportEmail = smtpSettings.Value.FromEmail;
    }

    protected override string TemplateName => "payment-refunded";

    protected override string SubjectFor(PaymentRefundedEvent message) => $"Refund processed for order #{message.OrderId}";

    protected override string? UserIdOf(PaymentRefundedEvent message) => message.UserId;

    protected override Task SendAsync(PaymentRefundedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendPaymentRefundedAsync(
            recipient,
            new PaymentRefundedEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = recipient.DisplayName ?? message.UserId,
                Amount = message.Amount,
                // Payment audit Stage 8b: the event says which currency was refunded; this used to hard-code USD.
                Currency = message.Currency,
                RefundedAt = message.RefundedAt,
                SupportEmail = _supportEmail
            },
            cancellationToken);
}
