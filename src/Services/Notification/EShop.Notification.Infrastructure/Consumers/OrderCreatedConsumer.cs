using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class OrderCreatedConsumer : NotificationConsumer<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;

    public OrderCreatedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        TimeProvider timeProvider,
        ILogger<OrderCreatedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
    }

    protected override string TemplateName => "order-created";

    protected override string SubjectFor(OrderCreatedEvent message) => $"Order confirmation #{message.OrderId}";

    protected override string? UserIdOf(OrderCreatedEvent message) => message.UserId;

    protected override Task<string> SendAsync(OrderCreatedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendOrderConfirmationAsync(
            recipient,
            new OrderConfirmationEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = GreetingName(recipient),
                OrderDate = message.OccurredOn,
                TotalAmount = message.TotalAmount,
                // Notification audit S7 (D11): the event names the currency (USD for a message published before it did).
                Currency = message.Currency,
                // S7 (L17): the units ordered, not the number of order lines.
                ItemCount = message.Items.Sum(item => item.Quantity)
            },
            cancellationToken);
}
