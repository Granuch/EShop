using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Notification.Application.Notifications.Common;
using MediatR;

namespace EShop.Notification.Application.Notifications.Commands.SendTestNotification;

/// <summary>
/// Sends one template, filled with sample data, to an address the operator names (Admin panel S13, endpoint #76).
/// The template comes from the route; the address and an optional greeting name from the body.
/// </summary>
public sealed record SendTestNotificationCommand : IRequest<Result<TestNotificationResultDto>>
{
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Where to send it. Personal data, so redacted from the request log like every other address here.</summary>
    [SensitiveData]
    public string? Email { get; init; }

    /// <summary>The name the email greets. Omitted: "there", as a real notification does for a customer with no name.</summary>
    public string? Name { get; init; }
}
