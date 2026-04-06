using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.Health;

public sealed class DownstreamHealthCheck : IHealthCheck
{
    private readonly IProxyConfigProvider _proxyConfigProvider;

    public DownstreamHealthCheck(IProxyConfigProvider proxyConfigProvider)
    {
        _proxyConfigProvider = proxyConfigProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var config = _proxyConfigProvider.GetConfig();

        if (config.Routes is null || config.Routes.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("No YARP routes configured."));
        }

        if (config.Clusters is null || config.Clusters.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("No YARP clusters configured."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("YARP routes and clusters are configured."));
    }
}
