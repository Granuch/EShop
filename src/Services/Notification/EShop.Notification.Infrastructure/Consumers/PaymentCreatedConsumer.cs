using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PaymentCreatedConsumer : IdempotentConsumer<PaymentCreatedEvent, NotificationDbContext>
{
    private const string TemplateName = "payment-created";

    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly IEmailService _emailService;
    private readonly IUserContactResolver _userContactResolver;

    public PaymentCreatedConsumer(
        NotificationDbContext dbContext,
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        ILogger<PaymentCreatedConsumer> logger)
        : base(dbContext, logger)
    {
        _notificationLogRepository = notificationLogRepository;
        _emailService = emailService;
        _userContactResolver = userContactResolver;
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentCreatedEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;
        var correlationId = context.CorrelationId?.ToString() ?? message.CorrelationId;

        if (await _notificationLogRepository.FindByEventIdAsync(message.EventId, cancellationToken) is not null)
        {
            Logger.LogInformation("Notification already processed for EventId={EventId}", message.EventId);
            return;
        }

        var recipient = await _userContactResolver.ResolveAsync(message.UserId, cancellationToken);

        var notificationLog = NotificationLog.CreatePending(
            message.EventId,
            nameof(PaymentCreatedEvent),
            correlationId,
            message.UserId,
            recipient?.Email ?? "unresolved@local",
            TemplateName,
            $"Payment received for order #{message.OrderId}");

        try
        {
            await _notificationLogRepository.AddAsync(notificationLog, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await _notificationLogRepository.FindByEventIdAsync(message.EventId, cancellationToken) is not null)
            {
                Logger.LogInformation("Notification already processed (concurrent duplicate) for EventId={EventId}", message.EventId);
                return;
            }
            throw;
        }

        if (recipient is null)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed("Recipient email could not be resolved.");
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);
            throw new InvalidOperationException("Recipient email could not be resolved.");
        }

        try
        {
            await _emailService.SendPaymentCreatedAsync(
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

            notificationLog.MarkSent(providerMessageId: null);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);
        }
        catch (Exception ex)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed(ex.Message);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);
            throw;
        }
    }
}
