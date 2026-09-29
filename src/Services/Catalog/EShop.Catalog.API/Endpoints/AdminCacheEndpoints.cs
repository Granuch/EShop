using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.BuildingBlocks.Infrastructure.SystemAdmin;
using EShop.Catalog.Application.Administration.Commands.InvalidateCacheFamilies;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EShop.Catalog.API.Endpoints;

/// <summary>
/// <c>POST /api/v1/admin/cache/invalidate</c> (admin panel S19, endpoint #88), the one System-page endpoint that is
/// proxied rather than served by the gateway: Catalog is the only service with service-wide cache families
/// (Ordering's are per user), so it is the only service the endpoint could act on.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>system.manage</c>, not the <c>Admin</c> role every other Catalog admin endpoint uses.</b> Flushing a cache is
/// an operation on the platform, not a catalog edit, and the permission vocabulary reserved <c>system.manage</c> for
/// exactly it. The gateway route <c>admin-cache-route</c> asks the <c>Admin</c> role question, as every YARP admin route
/// does (Basket's S14 route is the precedent); both must pass.
/// </para>
/// <para>
/// <b>A new path prefix is three coordinated changes</b>: this endpoint, the gateway route, and the
/// <c>/api/v1/admin/cache</c> prefix in <c>CatalogProxyGuardMiddleware</c>. <c>GatewayRouteAuthorizationTests</c> and
/// <c>ProxyGuardCoverageTests</c> fail when the second or third is missing.
/// </para>
/// </remarks>
public static class AdminCacheEndpoints
{
    public static IEndpointRouteBuilder MapAdminCacheEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/v1/admin/cache/invalidate?family=products:list — omit family for every Catalog family.
        app.MapPost(SystemAdminPaths.CacheInvalidate, async ([FromQuery] string? family, IMediator mediator) =>
        {
            var result = await mediator.Send(new InvalidateCacheFamiliesCommand { Family = family });

            return result.Match(
                value => Results.Ok(value),
                error => ProblemResults.For(error, error.Code == InvalidateCacheFamiliesCommandHandler.CacheUnavailableCode
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status400BadRequest));
        })
        .RequireAuthorization(EShopPermissions.SystemManage)
        .WithName("InvalidateCacheFamilies")
        .WithTags("Admin — System")
        .Produces<CacheInvalidationReport>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
