namespace EShop.ApiGateway.Simulation;

public interface ISimulationProfileProvider
{
    bool TryGetByRouteId(string routeId, out SimulationProfile profile);
    bool TryGetByPath(PathString requestPath, out SimulationProfile profile);
}
