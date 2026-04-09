using EShop.ApiGateway.Configuration;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Simulation;

public sealed class SimulationProfileProvider : ISimulationProfileProvider
{
    private readonly IReadOnlyDictionary<string, SimulationProfile> _byRouteId;
    private readonly IReadOnlyList<SimulationProfile> _profilesByPrefix;

    public SimulationProfileProvider(IOptions<SimulationOptions> options)
    {
        var routeOptions = options.Value.Routes;

        var byRouteId = new Dictionary<string, SimulationProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var (routeId, route) in routeOptions)
        {
            if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(route.PathPrefix))
            {
                continue;
            }

            var minDelay = Math.Max(0, route.DelayMs.Min);
            var maxDelay = Math.Max(minDelay, route.DelayMs.Max);
            var errorRate = Math.Clamp(route.ErrorRate, 0d, 1d);

            var profile = new SimulationProfile(
                RouteId: routeId,
                PathPrefix: route.PathPrefix,
                Enabled: route.Enabled,
                DelayMinMs: minDelay,
                DelayMaxMs: maxDelay,
                ErrorRate: errorRate,
                FailureModes: route.FailureModes,
                ForcedFailureMode: string.IsNullOrWhiteSpace(route.ForcedFailureMode) ? null : route.ForcedFailureMode,
                ResponseTemplate: string.IsNullOrWhiteSpace(route.ResponseTemplate) ? "default" : route.ResponseTemplate);

            byRouteId[routeId] = profile;
        }

        _byRouteId = byRouteId;
        _profilesByPrefix = byRouteId.Values
            .OrderByDescending(x => x.PathPrefix.Length)
            .ToArray();
    }

    public bool TryGetByRouteId(string routeId, out SimulationProfile profile)
    {
        return _byRouteId.TryGetValue(routeId, out profile!);
    }

    public bool TryGetByPath(PathString requestPath, out SimulationProfile profile)
    {
        var path = requestPath.Value;
        if (string.IsNullOrWhiteSpace(path))
        {
            profile = default!;
            return false;
        }

        profile = _profilesByPrefix.FirstOrDefault(x =>
            path.StartsWith(x.PathPrefix, StringComparison.OrdinalIgnoreCase))!;

        return profile is not null;
    }
}
