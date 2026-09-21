using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.Notification.Application.Notifications.Commands.MarkNotificationUndeliverable;
using EShop.Notification.Application.Notifications.Commands.ResendNotification;
using EShop.Notification.Application.Notifications.Commands.RetryFailedNotifications;
using EShop.Notification.Application.Notifications.Commands.SendTestNotification;
using EShop.Notification.Application.Notifications.Common;
using EShop.Notification.Application.Notifications.Queries.GetNotificationById;
using EShop.Notification.Application.Notifications.Queries.GetNotifications;
using EShop.Notification.Application.Notifications.Queries.GetNotificationStats;
using EShop.Notification.Application.Notifications.Queries.GetNotificationTemplates;
using MediatR;
using Microsoft.AspNetCore.Mvc;

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
/// <b>Reads declare <c>notifications.read</c>; every action declares <c>notifications.manage</c></b> (S13, #72–#76) —
/// including the template test send, which writes nothing but sends an email to an address the caller chooses. The
/// template list is a read.
/// </para>
///
/// <para>
/// The actions' failures go through <see cref="StatusFor"/>, one switch from error code to status, so two endpoints
/// cannot answer the same error with different statuses (Basket audit S8's shape).
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

        // #75. A literal segment, so it cannot be read as "/{id:guid}" whatever the declaration order.
        group.MapGet("/templates", async (IMediator mediator, CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new GetNotificationTemplatesQuery(), cancellationToken)))
        .WithName("GetNotificationTemplates")
        .RequireAuthorization(EShopPermissions.NotificationsRead)
        .Produces<IReadOnlyList<NotificationTemplateDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // #76. Synchronous: the answer (a Message-ID, or why the mail server would not take it) is the point.
        group.MapPost("/templates/{name}/test", async (
            string name,
            TestNotificationRequest? request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(
                new SendTestNotificationCommand { TemplateName = name, Email = request?.Email, Name = request?.Name },
                cancellationToken);

            return result.Match(sent => Results.Ok(sent), Problem);
        })
        .WithName("SendTestNotification")
        .RequireAuthorization(EShopPermissions.NotificationsManage)
        .Produces<TestNotificationResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // #73. 202: by the time this answers the rows have been handed to the consumer queues, not delivered.
        group.MapPost("/retry-failed", async (
            [FromBody] RetryFailedNotificationsCommand? command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            // An empty body is "the oldest failed ones, up to the cap", not a 400.
            var result = await mediator.Send(command ?? new RetryFailedNotificationsCommand(), cancellationToken);

            return result.Match(report => Results.Accepted(value: report), Problem);
        })
        .WithName("RetryFailedNotifications")
        .RequireAuthorization(EShopPermissions.NotificationsManage)
        .Produces<RetryFailedNotificationsResultDto>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // #72. 202 with the row as it stands and a Location to watch it change: the delivery happens in the consumer.
        group.MapPost("/{id:guid}/resend", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(new ResendNotificationCommand(id), cancellationToken);

            return result.Match(detail => Results.Accepted($"/api/v1/notifications/{id}", detail), Problem);
        })
        .WithName("ResendNotification")
        .RequireAuthorization(EShopPermissions.NotificationsManage)
        .Produces<NotificationDetailDto>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // #74. 409 for a final row, a live attempt, or — through AddEfConcurrency() — a delivery that claimed the row
        // between this request's read and its save.
        group.MapPost("/{id:guid}/mark-undeliverable", async (
            Guid id,
            MarkUndeliverableRequest? request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(
                new MarkNotificationUndeliverableCommand(id, request?.Reason), cancellationToken);

            return result.Match(detail => Results.Ok(detail), Problem);
        })
        .WithName("MarkNotificationUndeliverable")
        .RequireAuthorization(EShopPermissions.NotificationsManage)
        .Produces<NotificationDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Every action's failure status, in one place. An unknown code is a 500 on purpose: a new error that nobody mapped
    /// is a defect, and a 400 would blame the caller for it.
    /// </summary>
    private static int StatusFor(Error error) => error.Code switch
    {
        "Validation.Failed" => StatusCodes.Status400BadRequest,
        NotificationErrors.NotFoundCode or NotificationErrors.TemplateNotFoundCode => StatusCodes.Status404NotFound,
        NotificationErrors.FinalCode
            or NotificationErrors.AttemptInProgressCode
            or NotificationErrors.NotResendableCode => StatusCodes.Status409Conflict,
        NotificationErrors.BusUnavailableCode
            or NotificationErrors.DispatchFailedCode
            or NotificationErrors.TestSendFailedCode => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    private static IResult Problem(Error error) => ProblemResults.For(error, StatusFor(error));
}

/// <summary>The body of <c>POST /{id}/mark-undeliverable</c>.</summary>
public sealed record MarkUndeliverableRequest(string? Reason);

/// <summary>The body of <c>POST /templates/{name}/test</c>: where to send it, and whom it greets.</summary>
public sealed record TestNotificationRequest(string? Email, string? Name);
