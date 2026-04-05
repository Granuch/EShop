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

public sealed class OrderShippedConsumer : IdempotentConsumer<OrderShippedEvent, NotificationDbContext>
{
    private const string TemplateName = "order-shipped";

    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly IEmailService _emailService;
    private readonly IUserContactResolver _userContactResolver;

    public OrderShippedConsumer(
        NotificationDbContext dbContext,
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        ILogger<OrderShippedConsumer> logger)
        : base(dbContext, logger)
    {
        _notificationLogRepository = notificationLogRepository;
        _emailService = emailService;
        _userContactResolver = userContactResolver;
    }

    protected override async Task HandleAsync(ConsumeContext<OrderShippedEvent> context, CancellationToken cancellationToken)
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

        var recipient = !string.IsNullOrWhiteSpace(message.UserEmail)
            ? new RecipientAddress(message.UserEmail)
            : await _userContactResolver.ResolveAsync(message.UserId, cancellationToken);

        var notificationLog = NotificationLog.CreatePending(
            message.EventId,
            nameof(OrderShippedEvent),
            correlationId,
            message.UserId,
            recipient?.Email ?? "unresolved@local",
            TemplateName,
            $"Your order #{message.OrderId} has shipped");

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
            await _emailService.SendOrderShippedAsync(
                recipient,
                new OrderShippedEmailModel
                {
                    OrderId = message.OrderId,
                    CustomerName = recipient.DisplayName ?? message.UserId,
                    TrackingNumber = message.TrackingNumber,
                    EstimatedDelivery = message.ShippedAt.AddDays(5).ToString("yyyy-MM-dd")
                },
                cancellationToken);

            notificationLog.MarkSent(providerMessageId: null);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogInformation("Order shipped notification sent for OrderId={OrderId}", message.OrderId);
        }
        catch (Exception ex)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed(ex.Message);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogWarning(ex, "Order shipped notification failed for OrderId={OrderId}", message.OrderId);
            throw;
        }
    }
}
