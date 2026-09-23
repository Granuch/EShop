namespace EShop.ApiGateway.Simulation;

public interface ISimulationProfileProvider
{
    bool TryGetByRouteId(string routeId, out SimulationProfile profile);
    bool TryGetByPath(PathString requestPath, out SimulationProfile profile);

    /// <summary>
    /// Every profile, as the middleware sees it: clamped, and without the entries it skips for having no route id or
    /// path prefix. The System page's feature flags (admin panel S19) report these rather than the raw options, so the
    /// page shows what the gateway will actually do.
    /// </summary>
    IReadOnlyList<SimulationProfile> Profiles { get; }
}
