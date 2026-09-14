using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PaymentCompletedConsumer : NotificationConsumer<PaymentCompletedEvent>
{
    private readonly IEmailService _emailService;

    public PaymentCompletedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        TimeProvider timeProvider,
        ILogger<PaymentCompletedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
    }

    protected override string TemplateName => "payment-completed";

    protected override string SubjectFor(PaymentCompletedEvent message) => $"Payment successful for order #{message.OrderId}";

    protected override string? UserIdOf(PaymentCompletedEvent message) => message.UserId;

    protected override Task<string> SendAsync(PaymentCompletedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendPaymentCompletedAsync(
            recipient,
            new PaymentCompletedEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = GreetingName(recipient),
                Amount = message.Amount,
                Currency = message.Currency,
                CompletedAt = message.CompletedAt
            },
            cancellationToken);
}
