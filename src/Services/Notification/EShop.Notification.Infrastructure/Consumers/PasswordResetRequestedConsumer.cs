using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Consumers;

public sealed class PasswordResetRequestedConsumer : NotificationConsumer<PasswordResetRequestedIntegrationEvent>
{
    private readonly IEmailService _emailService;
    private readonly PasswordResetSettings _passwordResetSettings;

    public PasswordResetRequestedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        IOptions<PasswordResetSettings> passwordResetSettings,
        TimeProvider timeProvider,
        ILogger<PasswordResetRequestedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
        // PasswordReset:ResetUrlBase is checked once, at startup, by NotificationConfigurationGuard (Notification audit
        // S4, D4, L6). This constructor used to apply a second, looser rule of its own.
        _passwordResetSettings = passwordResetSettings.Value;
    }

    protected override string TemplateName => "password-reset";

    protected override string SubjectFor(PasswordResetRequestedIntegrationEvent message) => "Password reset request";

    protected override string? UserIdOf(PasswordResetRequestedIntegrationEvent message) => message.UserId;

    protected override Task<string> SendAsync(
        PasswordResetRequestedIntegrationEvent message,
        RecipientAddress recipient,
        CancellationToken cancellationToken)
        => _emailService.SendPasswordResetAsync(
            recipient,
            new PasswordResetEmailModel
            {
                CustomerName = GreetingName(recipient),
                ResetLink = BuildResetLink(message.UserId, message.ResetToken)
            },
            cancellationToken);

    private string BuildResetLink(string userId, string token)
    {
        var baseUrl = _passwordResetSettings.ResetUrlBase;

        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
    }
}
