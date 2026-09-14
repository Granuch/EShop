using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PaymentCreatedConsumer : NotificationConsumer<PaymentCreatedEvent>
{
    private readonly IEmailService _emailService;

    public PaymentCreatedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        TimeProvider timeProvider,
        ILogger<PaymentCreatedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
    }

    protected override string TemplateName => "payment-created";

    // Payment audit Stage 8 (M5). The event means an attempt started with nothing charged yet, and a Stripe customer may
    // never finish paying. It used to be announced as "Payment received".
    protected override string SubjectFor(PaymentCreatedEvent message) => $"Payment started for order #{message.OrderId}";

    protected override string? UserIdOf(PaymentCreatedEvent message) => message.UserId;

    protected override Task SendAsync(PaymentCreatedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendPaymentCreatedAsync(
            recipient,
            new PaymentCreatedEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = recipient.DisplayName ?? message.UserId,
                Amount = message.Amount,
                Currency = message.Currency,
                Status = message.Status,
                CreatedAt = message.CreatedAt
            },
            cancellationToken);
}
