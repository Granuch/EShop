using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.Consumers;

/// <summary>
/// Sends the email-confirmation link Identity requests at registration and on
/// <c>POST /api/v1/auth/resend-confirmation</c>. The same shape as <see cref="PasswordResetRequestedConsumer"/>: the
/// event carries a live token, so it is an <c>ISensitivePayloadEvent</c> — no payload is kept on the
/// <c>NotificationLogs</c> row, it cannot be resent by an operator, and a faulted message is discarded rather than parked
/// (<see cref="EmailConfirmationRequestedConsumerDefinition"/>). The user asks for a new link instead.
/// </summary>
public sealed class EmailConfirmationRequestedConsumer : NotificationConsumer<EmailConfirmationRequestedIntegrationEvent>
{
    private readonly IEmailService _emailService;
    private readonly EmailConfirmationSettings _settings;

    public EmailConfirmationRequestedConsumer(
        INotificationLogRepository notificationLogRepository,
        IEmailService emailService,
        IUserContactResolver userContactResolver,
        IOptions<EmailConfirmationSettings> settings,
        TimeProvider timeProvider,
        ILogger<EmailConfirmationRequestedConsumer> logger)
        : base(notificationLogRepository, userContactResolver, timeProvider, logger)
    {
        _emailService = emailService;
        // EmailConfirmation:ConfirmUrlBase is checked once, at startup, by NotificationConfigurationGuard.
        _settings = settings.Value;
    }

    protected override string TemplateName => NotificationTemplates.EmailConfirmation;

    protected override string SubjectFor(EmailConfirmationRequestedIntegrationEvent message) => "Confirm your email address";

    protected override string? UserIdOf(EmailConfirmationRequestedIntegrationEvent message) => message.UserId;

    protected override Task<string> SendAsync(
        EmailConfirmationRequestedIntegrationEvent message,
        RecipientAddress recipient,
        CancellationToken cancellationToken)
        => _emailService.SendEmailConfirmationAsync(
            recipient,
            new EmailConfirmationEmailModel
            {
                CustomerName = GreetingName(recipient),
                ConfirmationLink = ActionLinks.WithUserAndToken(
                    _settings.ConfirmUrlBase, message.UserId, message.ConfirmationToken)
            },
            cancellationToken);
}
