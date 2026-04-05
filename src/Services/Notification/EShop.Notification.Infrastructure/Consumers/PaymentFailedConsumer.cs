using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Data;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PaymentFailedConsumer : IdempotentConsumer<PaymentFailedEvent, NotificationDbContext>
{
    private const string TemplateName = "payment-failed";

    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly IEmailService _emailService;
    private readonly IUserContactResolver _userContactResolver;
    private readonly string _supportEmail;

    public PaymentFailedConsumer(
        NotificationDbContext dbContext,
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        IOptions<SmtpSettings> smtpSettings,
        ILogger<PaymentFailedConsumer> logger)
        : base(dbContext, logger)
    {
        _notificationLogRepository = notificationLogRepository;
        _emailService = emailService;
        _userContactResolver = userContactResolver;

        if (string.IsNullOrWhiteSpace(smtpSettings.Value.FromEmail))
        {
            throw new InvalidOperationException("Smtp:FromEmail must be configured for payment failure support contact.");
        }

        _supportEmail = smtpSettings.Value.FromEmail;
    }

    protected override async Task HandleAsync(ConsumeContext<PaymentFailedEvent> context, CancellationToken cancellationToken)
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

        if (string.IsNullOrWhiteSpace(message.UserId))
        {
            Logger.LogError("PaymentFailedEvent does not contain UserId. EventId={EventId}", message.EventId);
            throw new InvalidOperationException("PaymentFailedEvent.UserId is required for notification processing.");
        }

        var recipient = await _userContactResolver.ResolveAsync(message.UserId, cancellationToken);

        var notificationLog = NotificationLog.CreatePending(
            message.EventId,
            nameof(PaymentFailedEvent),
            correlationId,
            message.UserId,
            recipient?.Email ?? "unresolved@local",
            TemplateName,
            $"Payment failed for order #{message.OrderId}");

        await _notificationLogRepository.AddAsync(notificationLog, cancellationToken);

        if (recipient is null)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed("Recipient email could not be resolved.");
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogError("Recipient email resolution failed for UserId={UserId}", message.UserId);
            throw new InvalidOperationException("Recipient email could not be resolved.");
        }

        try
        {
            await _emailService.SendPaymentFailedAsync(
                recipient,
                new PaymentFailedEmailModel
                {
                    OrderId = message.OrderId,
                    CustomerName = recipient.DisplayName ?? message.UserId,
                    FailureReason = message.Reason,
                    SupportEmail = _supportEmail
                },
                cancellationToken);

            notificationLog.MarkSent(providerMessageId: null);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogInformation("Payment failed notification sent for OrderId={OrderId}", message.OrderId);
        }
        catch (Exception ex)
        {
            notificationLog.IncrementRetry();
            notificationLog.MarkFailed(ex.Message);
            await _notificationLogRepository.UpdateAsync(notificationLog, cancellationToken);

            Logger.LogWarning(ex, "Payment failed notification send attempt failed for OrderId={OrderId}", message.OrderId);

            if (context.GetRetryAttempt() >= 3)
            {
                Logger.LogError(ex, "Payment failed notification reached high retry count for EventId={EventId}", message.EventId);
            }

            throw;
        }
    }
}
