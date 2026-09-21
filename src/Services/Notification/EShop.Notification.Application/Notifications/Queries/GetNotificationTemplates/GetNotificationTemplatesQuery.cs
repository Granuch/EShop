using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Domain.Interfaces;
using MediatR;

namespace EShop.Notification.Application.Notifications.Queries.GetNotificationTemplates;

/// <summary>The email templates this service sends (Admin panel S13, endpoint #75). Cannot fail, so no <c>Result</c>.</summary>
public sealed record GetNotificationTemplatesQuery : IRequest<IReadOnlyList<NotificationTemplateDto>>;

public sealed class GetNotificationTemplatesQueryHandler
    : IRequestHandler<GetNotificationTemplatesQuery, IReadOnlyList<NotificationTemplateDto>>
{
    private readonly INotificationTemplateCatalog _catalog;

    public GetNotificationTemplatesQueryHandler(INotificationTemplateCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<IReadOnlyList<NotificationTemplateDto>> Handle(
        GetNotificationTemplatesQuery request,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<NotificationTemplateDto>>(_catalog.Templates
            .Select(t => new NotificationTemplateDto(t.Name, t.EventType, t.Resendable))
            .ToList());
}
