using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.Notification.Application.Notifications.Common;
using MediatR;

namespace EShop.Notification.Application.Notifications.Commands.ResendNotification;

/// <summary>
/// Sends one notification again (Admin panel S13, endpoint #72): its stored event goes back to this service's own
/// consumer queue, and the delivery path does the rest. Answers the notification as it stands when the resend is queued
/// — the delivery has not happened yet, which is why the endpoint says 202 rather than 200.
/// </summary>
public sealed record ResendNotificationCommand(Guid Id) : IRequest<Result<NotificationDetailDto>>, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Notification";

    string? IAuditedCommand.AuditEntityId => Id.ToString();
}
