using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.ApiGateway.Health;

public sealed class GatewayLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("Gateway process is alive."));
    }
}
