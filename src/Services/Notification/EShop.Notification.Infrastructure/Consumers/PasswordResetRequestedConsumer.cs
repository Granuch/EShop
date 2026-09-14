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
        _passwordResetSettings = passwordResetSettings.Value;

        if (string.IsNullOrWhiteSpace(_passwordResetSettings.ResetUrlBase)
            || !Uri.TryCreate(_passwordResetSettings.ResetUrlBase, UriKind.Absolute, out var parsedResetUri)
            || (parsedResetUri.Scheme != Uri.UriSchemeHttp && parsedResetUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("PasswordReset:ResetUrlBase must be configured as an absolute HTTP/HTTPS URL.");
        }
    }

    protected override string TemplateName => "password-reset";

    protected override string SubjectFor(PasswordResetRequestedIntegrationEvent message) => "Password reset request";

    protected override string? UserIdOf(PasswordResetRequestedIntegrationEvent message) => message.UserId;

    protected override Task SendAsync(
        PasswordResetRequestedIntegrationEvent message,
        RecipientAddress recipient,
        CancellationToken cancellationToken)
        => _emailService.SendPasswordResetAsync(
            recipient,
            new PasswordResetEmailModel
            {
                CustomerName = recipient.DisplayName ?? message.UserId,
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
