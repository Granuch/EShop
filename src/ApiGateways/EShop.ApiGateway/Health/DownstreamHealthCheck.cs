using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.Health;

public sealed class DownstreamHealthCheck : IHealthCheck
{
    private readonly IProxyConfigProvider _proxyConfigProvider;
    private readonly IHttpClientFactory _httpClientFactory;

    public DownstreamHealthCheck(
        IProxyConfigProvider proxyConfigProvider,
        IHttpClientFactory httpClientFactory)
    {
        _proxyConfigProvider = proxyConfigProvider;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var config = _proxyConfigProvider.GetConfig();

        if (config.Routes is null || config.Routes.Count == 0)
        {
            return HealthCheckResult.Unhealthy("No YARP routes configured.");
        }

        if (config.Clusters is null || config.Clusters.Count == 0)
        {
            return HealthCheckResult.Unhealthy("No YARP clusters configured.");
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(2);

        var failedDestinations = new List<string>();

        foreach (var cluster in config.Clusters)
        {
            if (cluster.Destinations is null || cluster.Destinations.Count == 0)
            {
                failedDestinations.Add($"{cluster.ClusterId}:no-destinations");
                continue;
            }

            foreach (var destination in cluster.Destinations.Values)
            {
                if (destination is null || string.IsNullOrWhiteSpace(destination.Address))
                {
                    failedDestinations.Add($"{cluster.ClusterId}:invalid-destination");
                    continue;
                }

                if (!Uri.TryCreate(destination.Address, UriKind.Absolute, out var baseUri))
                {
                    failedDestinations.Add($"{cluster.ClusterId}:invalid-uri");
                    continue;
                }

                var readinessUri = new Uri(baseUri, "health/ready");

                try
                {
                    using var response = await httpClient.GetAsync(readinessUri, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        failedDestinations.Add($"{cluster.ClusterId}:{readinessUri}=>{(int)response.StatusCode}");
                    }
                }
                catch (Exception)
                {
                    failedDestinations.Add($"{cluster.ClusterId}:{readinessUri}=>unreachable");
                }
            }
        }

        if (failedDestinations.Count > 0)
        {
            return HealthCheckResult.Unhealthy(
                description: "One or more downstream destinations are unhealthy.",
                data: new Dictionary<string, object>
                {
                    ["failedDestinations"] = failedDestinations
                });
        }

        return HealthCheckResult.Healthy("All downstream destinations are reachable and healthy.");
    }
}
