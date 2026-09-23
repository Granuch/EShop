using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Health;
using EShop.ApiGateway.Simulation;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.Http;
using EShop.BuildingBlocks.Infrastructure.SystemAdmin;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.SystemAdmin;

/// <summary>The whole platform's health: the worst component's status, and each component's checks.</summary>
public sealed record SystemHealthDto(string Status, DateTime CheckedAt, IReadOnlyList<ComponentHealthDto> Components);

/// <summary>
/// One component's health — its name, whether the gateway reached it, its overall status and a name/status pair per
/// check. Nothing else, deliberately: no description, no exception, no address (SEC-07).
/// </summary>
public sealed record ComponentHealthDto(string Name, bool Reachable, string Status, IReadOnlyList<EShopHealthCheckEntry> Checks)
{
    public static ComponentHealthDto Unreachable(string name) => new(name, Reachable: false, nameof(HealthStatus.Unhealthy), []);
}

/// <summary>
/// The read-only store settings (#85, decision Q9c): what actually governs pricing and payment today. A slice is
/// <c>null</c> when its service could not be read, and that service is named in <see cref="UnavailableServices"/>.
/// </summary>
public sealed record SystemSettingsDto(
    PricingSettingsDto? Pricing,
    PaymentSettingsDto? Payments,
    IReadOnlyList<string> UnavailableServices);

/// <summary>The read-only feature flags (#89): the gateway's fault injection and Payment's simulator.</summary>
public sealed record FeatureFlagsDto(
    GatewaySimulationFlagsDto GatewaySimulation,
    PaymentFeatureFlagsDto? Payments,
    IReadOnlyList<string> UnavailableServices);

/// <param name="Enabled"><c>Simulation:Enabled</c> — the master switch; no route simulates without it.</param>
/// <param name="AllowHeaderOverride">Whether a request's <c>X-Simulate</c> header may ask for simulation.</param>
/// <param name="Routes">Each simulation profile as the middleware will apply it.</param>
public sealed record GatewaySimulationFlagsDto(bool Enabled, bool AllowHeaderOverride, IReadOnlyList<SimulationRouteFlagDto> Routes);

/// <param name="Active">Whether this route injects faults today: its own switch and the master switch are both on.</param>
public sealed record SimulationRouteFlagDto(
    string RouteId,
    string PathPrefix,
    bool Enabled,
    bool Active,
    double ErrorRate,
    int DelayMinMs,
    int DelayMaxMs,
    string? ForcedFailureMode);

/// <summary>
/// The admin panel's System page (S19): aggregate health (#87), the read-only settings (#85) and the read-only feature
/// flags (#89), all served by the gateway itself under <c>system.manage</c>. The fourth System endpoint, cache
/// invalidation (#88), is Catalog's and reaches it through the <c>admin-cache-route</c> YARP route.
///
/// <para>
/// <b>Read-only, all three, and that is a decision, not a gap.</b> Q9c kept settings in configuration, and S19 applied
/// the same rule to feature flags: the gateway has no store to keep a runtime override in, an in-process one would be
/// lost on restart and diverge between replicas, and a fault-injection switch behind an admin API is a button that
/// fails production traffic. Changing any of them stays a redeploy.
/// </para>
///
/// <para>
/// These are endpoints, not YARP routes, so the routing-table tests cannot see them; their policies are pinned by
/// <c>SystemEndpointsTests</c> through <c>EndpointDataSource</c>, as the audit endpoint's are.
/// </para>
/// </summary>
public static class GatewaySystemEndpoints
{
    public static IEndpointRouteBuilder MapGatewaySystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(SystemAdminPaths.Health, ReadHealthAsync)
            .RequireAuthorization(EShopPermissions.SystemManage)
            .WithName("GetSystemHealth")
            .WithTags("Admin — System")
            .Produces<SystemHealthDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet(SystemAdminPaths.Settings, ReadSettingsAsync)
            .RequireAuthorization(EShopPermissions.SystemManage)
            .WithName("GetSystemSettings")
            .WithTags("Admin — System")
            .Produces<SystemSettingsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet(SystemAdminPaths.FeatureFlags, ReadFeatureFlagsAsync)
            .RequireAuthorization(EShopPermissions.SystemManage)
            .WithName("GetFeatureFlags")
            .WithTags("Admin — System")
            .Produces<FeatureFlagsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// Always 200, even when a component is down: the answer is a report for a screen, and the status a probe would read
    /// is the body's. The services' own <c>/health</c> endpoints keep answering 503 for load balancers.
    /// </summary>
    private static async Task<IResult> ReadHealthAsync(
        HttpContext httpContext,
        HealthCheckService gatewayChecks,
        SystemFanOut fanOut)
    {
        var cancellationToken = httpContext.RequestAborted;

        // The gateway's own checks minus "downstream": that check probes every service's readiness, which the fan-out
        // below reports per service, so running it too would double every downstream call and add nothing.
        var own = gatewayChecks.CheckHealthAsync(c => c.Name != DownstreamHealthCheck.Name, cancellationToken);
        var services = Task.WhenAll(SystemFanOut.Sources.Select(s => fanOut.GetHealthAsync(s.Name, cancellationToken)));
        await Task.WhenAll(own, services);

        var gateway = new ComponentHealthDto(
            "gateway",
            Reachable: true,
            own.Result.Status.ToString(),
            own.Result.Entries.Select(e => new EShopHealthCheckEntry { Name = e.Key, Status = e.Value.Status.ToString() }).ToList());

        var components = new[] { gateway }.Concat(services.Result).ToList();

        // HealthStatus orders Unhealthy < Degraded < Healthy, so the lowest is the worst.
        var worst = components.Min(c => Enum.Parse<HealthStatus>(c.Status));

        return Results.Ok(new SystemHealthDto(worst.ToString(), DateTime.UtcNow, components));
    }

    private static async Task<IResult> ReadSettingsAsync(HttpContext httpContext, SystemFanOut fanOut)
    {
        var pricing = fanOut.GetAsCallerAsync<PricingSettingsDto>("ordering", SystemAdminPaths.Settings, httpContext);
        var payments = fanOut.GetAsCallerAsync<PaymentSettingsDto>("payment", SystemAdminPaths.Settings, httpContext);
        await Task.WhenAll(pricing, payments);

        return Results.Ok(new SystemSettingsDto(
            pricing.Result,
            payments.Result,
            Unavailable(("ordering", pricing.Result), ("payment", payments.Result))));
    }

    private static async Task<IResult> ReadFeatureFlagsAsync(
        HttpContext httpContext,
        SystemFanOut fanOut,
        IOptions<SimulationOptions> simulationOptions,
        ISimulationProfileProvider simulationProfiles)
    {
        var payments = await fanOut.GetAsCallerAsync<PaymentFeatureFlagsDto>("payment", SystemAdminPaths.FeatureFlags, httpContext);

        var options = simulationOptions.Value;
        var gateway = new GatewaySimulationFlagsDto(
            options.Enabled,
            options.AllowHeaderOverride,
            simulationProfiles.Profiles
                .OrderBy(p => p.RouteId, StringComparer.Ordinal)
                .Select(p => new SimulationRouteFlagDto(
                    p.RouteId,
                    p.PathPrefix,
                    p.Enabled,
                    Active: options.Enabled && p.Enabled,
                    p.ErrorRate,
                    p.DelayMinMs,
                    p.DelayMaxMs,
                    p.ForcedFailureMode))
                .ToList());

        return Results.Ok(new FeatureFlagsDto(gateway, payments, Unavailable(("payment", payments))));
    }

    private static IReadOnlyList<string> Unavailable(params (string Service, object? Slice)[] slices)
        => slices.Where(s => s.Slice is null).Select(s => s.Service).ToList();
}
