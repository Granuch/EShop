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

    protected override Task SendAsync(OrderCreatedEvent message, RecipientAddress recipient, CancellationToken cancellationToken)
        => _emailService.SendOrderConfirmationAsync(
            recipient,
            new OrderConfirmationEmailModel
            {
                OrderId = message.OrderId,
                CustomerName = recipient.DisplayName ?? message.UserId,
                OrderDate = message.OccurredOn,
                TotalAmount = message.TotalAmount,
                ItemCount = message.Items.Count
            },
            cancellationToken);
}
