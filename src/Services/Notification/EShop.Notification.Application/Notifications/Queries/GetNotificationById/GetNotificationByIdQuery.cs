using EShop.BuildingBlocks.Application;
using EShop.Notification.Application.Notifications.Common;
using MediatR;

namespace EShop.Notification.Application.Notifications.Queries.GetNotificationById;

/// <summary>One notification in full (Admin panel S12, endpoint #71).</summary>
public sealed record GetNotificationByIdQuery(Guid Id) : IRequest<Result<NotificationDetailDto>>;
