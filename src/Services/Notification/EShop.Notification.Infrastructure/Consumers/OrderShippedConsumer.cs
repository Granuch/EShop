using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class OrderShippedConsumer : NotificationConsumer<OrderShippedEvent>
{
    private readonly IEmailService _emailService;

    public OrderShippedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        TimeProvider timeProvider,
        ILogger<OrderShippedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
    }

    protected override string TemplateName => "order-shipped";

    protected override string SubjectFor(OrderShippedEvent message) => $"Your order #{message.OrderId} has shipped";

    protected override string? UserIdOf(OrderShippedEvent message) => message.UserId;

    /// <summary>The event may carry the address itself, which saves the Identity lookup.</summary>
    protected override RecipientAddress? RecipientFromEvent(OrderShippedEvent message)
        => string.IsNullOrWhiteSpace(message.UserEmail) ? null : new RecipientAddress(message.UserEmail);

    protected override Task<string> SendAsync(OrderShippedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendOrderShippedAsync(
            recipient,
            new OrderShippedEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = GreetingName(recipient),
                TrackingNumber = message.TrackingNumber,
                // Notification audit S7 (L18): the ship date the event carries, instead of an invented delivery date.
                ShippedAt = message.ShippedAt
            },
            cancellationToken);
}
