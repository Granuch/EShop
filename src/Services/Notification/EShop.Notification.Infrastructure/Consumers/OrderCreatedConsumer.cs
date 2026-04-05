using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class OrderCreatedConsumer : IdempotentConsumer<OrderCreatedEvent, NotificationDbContext>
{
    private const string TemplateName = "order-created";

    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly IEmailService _emailService;
    private readonly IUserContactResolver _userContactResolver;

    public OrderCreatedConsumer(
        NotificationDbContext dbContext,
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        ILogger<OrderCreatedConsumer> logger)
        : base(dbContext, logger)
    {
        _notificationLogRepository = notificationLogRepository;
        _emailService = emailService;
        _userContactResolver = userContactResolver;
    }

    protected override async Task HandleAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;
        var correlationId = context.CorrelationId?.ToString() ?? message.CorrelationId;

        using var scope = Logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = message.EventId,
            ["CorrelationId"] = correlationId,
            ["UserId"] = message.UserId
        });

        if (await _notificationLogRepository.FindByEventIdAsync(message.EventId, cancellationToken) is not null)
        {
            Logger.LogInformation("Notification already processed for EventId={EventId}", message.EventId);
            return;
        }

        var recipient = await _userContactResolver.ResolveAsync(message.UserId, cancellationToken);
        var notificationLog = NotificationLog.CreatePending(
            message.EventId,
            nameof(OrderCreatedEvent),
            correlationId,
            message.UserId,
            recipient?.Email ?? "unresolved@local",
            TemplateName,
            $"Order confirmation #{message.OrderId}");

        await _notificationLogRepository.AddAsync(notificationLog, cancellationToken);

        if (recipient is null)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed("Recipient email could not be resolved.");
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogWarning("Recipient email resolution failed for UserId={UserId}", message.UserId);
            throw new InvalidOperationException("Recipient email could not be resolved.");
        }

        try
        {
            await _emailService.SendOrderConfirmationAsync(
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

            notificationLog.MarkSent(providerMessageId: null);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogInformation("Order confirmation notification sent for OrderId={OrderId}", message.OrderId);
        }
        catch (Exception ex)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed(ex.Message);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogWarning(ex, "Order confirmation notification failed for OrderId={OrderId}", message.OrderId);
            throw;
        }
    }
}
