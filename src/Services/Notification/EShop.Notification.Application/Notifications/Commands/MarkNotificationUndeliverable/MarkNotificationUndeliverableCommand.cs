using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.Notification.Application.Notifications.Common;
using MediatR;

namespace EShop.Notification.Application.Notifications.Commands.MarkNotificationUndeliverable;

/// <summary>
/// An operator ends a notification for good, with a reason (Admin panel S13, endpoint #74, risk A5). The reason is
/// required: an Undeliverable row with no explanation is indistinguishable from a bug, and it is the one thing the next
/// person looking at the journal will need.
/// </summary>
public sealed record MarkNotificationUndeliverableCommand(Guid Id, string? Reason)
    : IRequest<Result<NotificationDetailDto>>, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Notification";

    string? IAuditedCommand.AuditEntityId => Id.ToString();

    /// <summary>The longest reason accepted — far inside <c>LastError</c>'s 4000, prefix included.</summary>
    public const int MaxReasonLength = 500;
}
