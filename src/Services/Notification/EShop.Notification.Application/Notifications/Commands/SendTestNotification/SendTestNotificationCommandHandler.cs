using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Notification.Application.Notifications.Commands.SendTestNotification;

/// <summary>
/// Synchronous, unlike a resend: the operator is waiting to see whether the mail server takes it, and the answer — a
/// Message-ID, or why not — is the point of the button. One email, so one SMTP round trip is a fair request.
/// </summary>
public sealed class SendTestNotificationCommandHandler
    : IRequestHandler<SendTestNotificationCommand, Result<TestNotificationResultDto>>
{
    private readonly INotificationTemplateCatalog _catalog;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<SendTestNotificationCommandHandler> _logger;

    public SendTestNotificationCommandHandler(
        INotificationTemplateCatalog catalog,
        ICurrentUserContext currentUser,
        ILogger<SendTestNotificationCommandHandler> logger)
    {
        _catalog = catalog;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<TestNotificationResultDto>> Handle(
        SendTestNotificationCommand request,
        CancellationToken cancellationToken)
    {
        // Case-insensitively, then by the catalog's own spelling: the route is typed by a person, the template file
        // name is not.
        var template = _catalog.Templates.FirstOrDefault(
            t => string.Equals(t.Name, request.TemplateName, StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            return Result<TestNotificationResultDto>.Failure(new Error(
                NotificationErrors.TemplateNotFoundCode,
                $"There is no template named '{request.TemplateName}'."));
        }

        var recipient = new RecipientAddress(request.Email!, request.Name);

        string providerMessageId;
        try
        {
            providerMessageId = await _catalog.SendTestAsync(template.Name, recipient, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The SMTP client's own message names hosts and ports; it goes to the log, and the caller gets ours.
            _logger.LogWarning(ex, "A test send of template {TemplateName} failed.", template.Name);
            return Result<TestNotificationResultDto>.Failure(new Error(
                NotificationErrors.TestSendFailedCode,
                "The test email could not be sent: the mail server refused it or could not be reached. The reason is "
                + "in the service log."));
        }

        // The recipient is personal data and is not logged; the actor and the Message-ID are enough to find it.
        _logger.LogInformation(
            "Operator {ActorId} sent a test of template {TemplateName}. MessageId={MessageId}.",
            _currentUser.UserId, template.Name, providerMessageId);

        return Result<TestNotificationResultDto>.Success(new TestNotificationResultDto(template.Name, providerMessageId));
    }
}
