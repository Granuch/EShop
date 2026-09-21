using EShop.BuildingBlocks.Application.Pagination;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Application.Notifications.Queries.GetNotificationById;
using EShop.Notification.Application.Notifications.Queries.GetNotifications;
using EShop.Notification.Application.Notifications.Queries.GetNotificationStats;
using MediatR;

namespace EShop.Notification.API.Endpoints;

/// <summary>
/// The delivery journal (Admin panel S12, endpoints #70, #71, #77) — the first HTTP surface this service has ever had
/// beyond health and metrics.
///
/// <para>
/// <b>Everything here declares a permission, not the <c>Admin</c> role</b> (decision Q4c, §12.1 of the plan).
/// Notification has no <c>"Admin"</c> policy of its own to reach for, which is precisely the case the root guide names
/// for preferring a permission — Identity was the other one. <c>RolePermissionBundles</c> maps <c>Admin</c> to every
/// permission, so an existing admin token works unchanged.
/// </para>
///
/// <para>
/// Writes (resend, retry, mark-undeliverable, template test) belong to S13 and will declare
/// <c>EShopPermissions.NotificationsManage</c>; nothing here mutates anything.
/// </para>
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications")
            .WithTags("Notifications");

        // #70. Paged, newest first. The page size is capped at 100 by the validator: the retention window keeps 90
        // days of rows, which for a busy stack is more than any one response should carry.
        group.MapGet("/", async (
            [AsParameters] GetNotificationsQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);

            return result.Match(
                page => Results.Ok(page),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetNotifications")
        .RequireAuthorization(EShopPermissions.NotificationsRead)
        .Produces<PagedResult<NotificationSummaryDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // #77. Declared before "/{id:guid}" for readability only — the route constraint is what keeps "stats" from
        // being read as a notification id, not the declaration order.
        group.MapGet("/stats", async (
            [AsParameters] GetNotificationStatsQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);

            return result.Match(
                stats => Results.Ok(stats),
                error => ProblemResults.For(error, StatusCodes.Status400BadRequest));
        })
        .WithName("GetNotificationStats")
        .RequireAuthorization(EShopPermissions.NotificationsRead)
        .Produces<NotificationStatsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // #71. A malformed id never reaches the handler: the {id:guid} constraint fails to match and routing answers a
        // bare 404, which is correct and unrelated to the Result-path 404 below.
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new GetNotificationByIdQuery(id), cancellationToken);

            return result.Match(
                detail => Results.Ok(detail),
                error => ProblemResults.For(error, StatusCodes.Status404NotFound));
        })
        .WithName("GetNotificationById")
        .RequireAuthorization(EShopPermissions.NotificationsRead)
        .Produces<NotificationDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
