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

public sealed class PasswordResetRequestedConsumer : IdempotentConsumer<PasswordResetRequestedIntegrationEvent, NotificationDbContext>
{
    private const string TemplateName = "password-reset";

    private readonly INotificationLogRepository _notificationLogRepository;
    private readonly IUserContactResolver _userContactResolver;
    private readonly IEmailService _emailService;
    private readonly PasswordResetSettings _passwordResetSettings;

    public PasswordResetRequestedConsumer(
        NotificationDbContext dbContext,
        INotificationLogRepository notificationLogRepository,
        IUserContactResolver userContactResolver,
        IEmailService emailService,
        IOptions<PasswordResetSettings> passwordResetSettings,
        ILogger<PasswordResetRequestedConsumer> logger)
        : base(dbContext, logger)
    {
        _notificationLogRepository = notificationLogRepository;
        _userContactResolver = userContactResolver;
        _emailService = emailService;
        _passwordResetSettings = passwordResetSettings.Value;
    }

    protected override async Task HandleAsync(ConsumeContext<PasswordResetRequestedIntegrationEvent> context, CancellationToken cancellationToken)
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
            nameof(PasswordResetRequestedIntegrationEvent),
            correlationId,
            message.UserId,
            recipient?.Email ?? "unresolved@local",
            TemplateName,
            "Password reset request");

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

        var resetLink = BuildResetLink(message.UserId, message.ResetToken);

        try
        {
            await _emailService.SendPasswordResetAsync(
                recipient,
                new PasswordResetEmailModel
                {
                    CustomerName = recipient.DisplayName ?? message.UserId,
                    ResetLink = resetLink
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

    private string BuildResetLink(string userId, string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_passwordResetSettings.ResetUrlBase)
            ? "http://localhost:3000/reset-password"
            : _passwordResetSettings.ResetUrlBase;

        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
    }
}
