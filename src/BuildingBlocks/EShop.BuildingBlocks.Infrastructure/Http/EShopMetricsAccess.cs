using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Restricts the Prometheus scrape endpoints (<c>/prometheus</c>, <c>/metrics</c>) to callers on
/// internal networks.
///
/// Every component served both anonymously and unfiltered (SEC-07). Prometheus metrics are not
/// secrets in themselves, but in aggregate they disclose route inventory, traffic volumes, error
/// rates, queue depths and version/runtime detail — a reconnaissance surface that has no reason
/// to be reachable from outside the deployment.
///
/// <para>
/// <b>The default is chosen so it cannot break a working scrape.</b> With nothing configured,
/// loopback plus the private ranges (RFC 1918 and IPv6 unique-local) are allowed, which covers
/// every scrape path in this repo: the compose bridge network (172.16/12), a Kubernetes pod CIDR
/// (10/8), and localhost. What it stops is a metrics endpoint answering the public internet
/// because a port was published or an ingress rule was too broad. Set
/// <c>Metrics:AllowedNetworks</c> (a list of CIDRs) to narrow or widen that deliberately.
/// </para>
/// </summary>
public static class EShopMetricsAccess
{
    private const string ConfigurationSection = "Metrics:AllowedNetworks";

    /// <summary>
    /// Blocks metrics requests from outside the allowed networks with a bare 404 — not a 403,
    /// which would confirm the endpoint exists.
    /// </summary>
    /// <remarks>
    /// Register immediately before the metrics endpoints are mapped. Testing is exempt in full:
    /// <c>TestServer</c> has no socket, so <c>RemoteIpAddress</c> is null there and every request
    /// would otherwise be rejected.
    /// </remarks>
    public static IApplicationBuilder UseEShopMetricsAccess(
        this IApplicationBuilder app,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsEnvironment("Testing"))
        {
            return app;
        }

        var configured = configuration.GetSection(ConfigurationSection).Get<string[]>() ?? [];
        var allowedNetworks = ParseNetworks(configured);
        var usingDefaults = allowedNetworks.Count == 0;

        Log.Information(
            usingDefaults
                ? "Metrics endpoints restricted to loopback and private networks (no {Section} configured)."
                : "Metrics endpoints restricted to {Count} configured network(s) from {Section}.",
            usingDefaults ? ConfigurationSection : allowedNetworks.Count,
            ConfigurationSection);

        return app.Use(async (context, next) =>
        {
            if (!IsMetricsPath(context.Request.Path))
            {
                await next();
                return;
            }

            if (IsAllowed(context.Connection.RemoteIpAddress, allowedNetworks))
            {
                await next();
                return;
            }

            Log.Warning(
                "Rejected metrics request from {RemoteIp} for {Path}",
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                context.Request.Path.Value);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
        });
    }

    private static bool IsMetricsPath(PathString path) =>
        path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/prometheus", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowed(IPAddress? remoteIp, IReadOnlyList<IPNetwork> allowedNetworks)
    {
        if (remoteIp is null)
        {
            // No socket behind the request. In practice this is an in-process host; there is no
            // remote caller to keep out.
            return true;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        if (allowedNetworks.Count > 0)
        {
            return allowedNetworks.Any(network => network.Contains(remoteIp));
        }

        return IsLoopbackOrPrivate(remoteIp);
    }

    private static bool IsLoopbackOrPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            return octets[0] switch
            {
                10 => true,                                     // 10.0.0.0/8
                172 => octets[1] >= 16 && octets[1] <= 31,      // 172.16.0.0/12 (Docker default)
                192 => octets[1] == 168,                        // 192.168.0.0/16
                169 => octets[1] == 254,                        // 169.254.0.0/16 link-local
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7 unique-local, plus link-local.
            var first = address.GetAddressBytes()[0];
            return (first & 0xFE) == 0xFC || address.IsIPv6LinkLocal;
        }

        return false;
    }

    private static List<IPNetwork> ParseNetworks(IEnumerable<string> configured)
    {
        var networks = new List<IPNetwork>();

        foreach (var entry in configured)
        {
            if (IPNetwork.TryParse(entry, out var network))
            {
                networks.Add(network);
            }
            else
            {
                Log.Error(
                    "Ignoring unparseable CIDR '{Entry}' in {Section}. Metrics access will fall back to the remaining entries.",
                    entry,
                    ConfigurationSection);
            }
        }

        return networks;
    }
}
