using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PaymentRefundedConsumer : IdempotentConsumer<PaymentRefundedEvent, NotificationDbContext>
{
    private const string TemplateName = "payment-refunded";

    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly IEmailService _emailService;
    private readonly IUserContactResolver _userContactResolver;
    private readonly string _supportEmail;

    public PaymentRefundedConsumer(
        NotificationDbContext dbContext,
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        IOptions<SmtpSettings> smtpSettings,
        ILogger<PaymentRefundedConsumer> logger)
        : base(dbContext, logger)
    {
        _notificationLogRepository = notificationLogRepository;
        _emailService = emailService;
        _userContactResolver = userContactResolver;

        if (string.IsNullOrWhiteSpace(smtpSettings.Value.FromEmail))
        {
            throw new InvalidOperationException("Smtp:FromEmail must be configured for refund support contact.");
        }

        _supportEmail = smtpSettings.Value.FromEmail;
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentRefundedEvent> context, CancellationToken cancellationToken)
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
            nameof(PaymentRefundedEvent),
            correlationId,
            message.UserId,
            recipient?.Email ?? "unresolved@local",
            TemplateName,
            $"Refund processed for order #{message.OrderId}");

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

            Logger.LogWarning("Recipient email resolution failed for UserId={UserId}", message.UserId);
            throw new InvalidOperationException("Recipient email could not be resolved.");
        }

        try
        {
            await _emailService.SendPaymentRefundedAsync(
                recipient,
                new PaymentRefundedEmailModel
                {
                    OrderId = message.OrderId,
                    CustomerName = recipient.DisplayName ?? message.UserId,
                    Amount = message.Amount,
                    Currency = "USD",
                    RefundedAt = message.RefundedAt,
                    SupportEmail = _supportEmail
                },
                cancellationToken);

            notificationLog.MarkSent(providerMessageId: null);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogInformation("Payment refunded notification sent for OrderId={OrderId}", message.OrderId);
        }
        catch (Exception ex)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed(ex.Message);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogWarning(ex, "Payment refunded notification send attempt failed for OrderId={OrderId}", message.OrderId);
            throw;
        }
    }
}
