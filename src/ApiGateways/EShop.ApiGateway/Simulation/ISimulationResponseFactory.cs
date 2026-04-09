namespace EShop.ApiGateway.Simulation;

public interface ISimulationResponseFactory
{
    Task WriteAsync(HttpContext context, SimulationProfile profile, CancellationToken cancellationToken);
}
