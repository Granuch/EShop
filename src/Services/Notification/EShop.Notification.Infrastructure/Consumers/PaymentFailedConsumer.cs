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

        if (string.IsNullOrWhiteSpace(smtpSettings.Value.FromEmail))
        {
            throw new InvalidOperationException("Smtp:FromEmail must be configured for payment failure support contact.");
        }

        _supportEmail = smtpSettings.Value.FromEmail;
    }

    protected override string TemplateName => "payment-failed";

    protected override string SubjectFor(PaymentFailedEvent message) => $"Payment failed for order #{message.OrderId}";

    protected override string? UserIdOf(PaymentFailedEvent message) => message.UserId;

    protected override Task SendAsync(PaymentFailedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendPaymentFailedAsync(
            recipient,
            new PaymentFailedEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = recipient.DisplayName ?? message.UserId,
                FailureReason = message.Reason,
                SupportEmail = _supportEmail
            },
            cancellationToken);
}
