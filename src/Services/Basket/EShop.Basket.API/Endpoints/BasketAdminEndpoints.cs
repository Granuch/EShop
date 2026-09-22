using EShop.Basket.Application.Queries.Admin;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using MediatR;

namespace EShop.Basket.API.Endpoints;

/// <summary>
/// The admin panel's basket reads (Admin panel S14): every stored basket (#78), the abandoned ones (#79) and the outbox's
/// dead letters in detail (#81). Read-only by decision Q7a — an admin sees carts and changes none.
///
/// <para><b>Permission policies, not the <c>Admin</c> role</b>, as every endpoint the admin panel adds declares (plan
/// §12.1). The two S7 outbox endpoints beside these keep <c>RequireAuthorization("Admin")</c> until a stage changes them,
/// as Payment's older admin endpoints do. <c>RolePermissionBundles</c> gives the <c>Admin</c> role every permission, so an
/// administrator's token opens both. The gateway asks the role question as well (<c>basket-admin-route</c>); both must
/// pass.</para>
/// </summary>
public static class BasketAdminEndpoints
{
    public static void MapBasketAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var carts = app.MapGroup("/api/v1/basket/admin")
            .WithTags("Basket (admin)")
            .RequireAuthorization(EShopPermissions.BasketsRead);

        carts.MapGet("/carts", async (
            [AsParameters] GetBasketsQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);
            return result.Match(page => Results.Ok(page), BasketEndpoints.Problem);
        })
        .WithName("ListStoredBaskets")
        .Produces<AdminBasketPageDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        carts.MapGet("/abandoned", async (
            [AsParameters] GetAbandonedBasketsQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);
            return result.Match(page => Results.Ok(page), BasketEndpoints.Problem);
        })
        .WithName("ListAbandonedBaskets")
        .Produces<AdminBasketPageDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Its own group: the S7 outbox group requires the Admin role for all it maps, and layering that onto this would
        // make a system.manage grant without the role useless here.
        var outbox = app.MapGroup("/api/v1/basket/admin/outbox")
            .WithTags("Basket outbox (admin)")
            // The permission that already names outbox dead-letter replay: whoever reads these is whoever replays them.
            .RequireAuthorization(EShopPermissions.SystemManage);

        outbox.MapGet("/dead-letters/details", async (
            [AsParameters] GetOutboxDeadLettersQuery query,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.Send(query, cancellationToken);
            return result.Match(page => Results.Ok(page), BasketEndpoints.Problem);
        })
        .WithName("ListOutboxDeadLetters")
        .Produces<OutboxDeadLetterPageDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
