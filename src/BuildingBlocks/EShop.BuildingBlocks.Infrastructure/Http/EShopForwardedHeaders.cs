using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Shared configuration for trusting <c>X-Forwarded-For</c> behind the YARP gateway.
///
/// Behind the gateway every request arrives from the gateway's address, so without trusting the
/// forwarded headers every IP-derived control — rate-limit partitions, brute-force tracking,
/// LastLoginIp — collapses onto a single value.
///
/// <para>
/// <b>KnownProxies vs KnownNetworks.</b> <c>KnownProxies</c> pins an individual proxy address,
/// which does not exist under Docker or Kubernetes where the gateway's address is assigned from a
/// bridge or pod CIDR and is not stable. <c>KnownNetworks</c> is the option that works there.
/// Reading only <c>KnownProxies</c> — as every service except Identity used to — means
/// <c>ForwardedHeaders:KnownNetworks</c> settings are silently ignored, including the CIDRs that
/// <c>k8s/02-configmap.yaml</c> sets namespace-wide.
/// </para>
/// </summary>
public static class EShopForwardedHeaders
{
    /// <summary>
    /// Reads <c>ForwardedHeaders:KnownProxies</c> and <c>ForwardedHeaders:KnownNetworks</c> and,
    /// when at least one valid entry exists, configures <see cref="ForwardedHeadersOptions"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> when forwarded headers were configured, so the caller knows whether to invoke
    /// <see cref="UseEShopForwardedHeaders"/>. Calling <c>UseForwardedHeaders</c> with no known
    /// proxy or network is not merely useless, it is unsafe: the middleware would then honour a
    /// client-supplied <c>X-Forwarded-For</c>.
    /// </returns>
    public static bool AddEShopForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredProxies = configuration
            .GetSection("ForwardedHeaders:KnownProxies")
            .Get<string[]>() ?? [];
        var configuredNetworks = configuration
            .GetSection("ForwardedHeaders:KnownNetworks")
            .Get<string[]>() ?? [];

        var knownProxies = new List<IPAddress>();
        foreach (var proxy in configuredProxies)
        {
            if (IPAddress.TryParse(proxy, out var ipAddress))
            {
                knownProxies.Add(ipAddress);
            }
            else
            {
                // Silently dropping these is how an unsubstituted deployment token such as
                // "#{TRUSTED_PROXY_IP}#" disables forwarded headers without anyone noticing.
                Log.Error(
                    "ForwardedHeaders:KnownProxies entry '{Entry}' is not a valid IP address and was ignored.",
                    proxy);
            }
        }

        // System.Net.IPNetwork, not the obsolete Microsoft.AspNetCore.HttpOverrides.IPNetwork.
        var knownNetworks = new List<System.Net.IPNetwork>();
        foreach (var network in configuredNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork))
            {
                knownNetworks.Add(parsedNetwork);
            }
            else
            {
                Log.Error(
                    "ForwardedHeaders:KnownNetworks entry '{Entry}' is not a valid CIDR range and was ignored.",
                    network);
            }
        }

        if (knownProxies.Count == 0 && knownNetworks.Count == 0)
        {
            Log.Warning(
                "Forwarded headers are not configured with known proxies or networks. X-Forwarded-For will be ignored " +
                "and every IP-derived control (rate limiting, brute-force protection) will see the proxy address.");
            return false;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var proxy in knownProxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in knownNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        Log.Information(
            "Forwarded headers enabled: {ProxyCount} known proxies, {NetworkCount} known networks.",
            knownProxies.Count,
            knownNetworks.Count);

        return true;
    }

    /// <summary>
    /// Applies the forwarded-headers middleware when <see cref="AddEShopForwardedHeaders"/>
    /// reported that it was configured. Must run before anything that reads the client address —
    /// rate limiting, authentication, request logging.
    /// </summary>
    public static IApplicationBuilder UseEShopForwardedHeaders(this IApplicationBuilder app, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (enabled)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    /// <summary>
    /// Partition key for per-client rate limiting: the caller's address, or <c>"anonymous"</c>
    /// when there is none (TestServer has no socket, so this is the common case under
    /// WebApplicationFactory).
    ///
    /// <para>
    /// IPv4-mapped IPv6 addresses are normalised so <c>::ffff:1.2.3.4</c> and <c>1.2.3.4</c> share
    /// one partition rather than getting a bucket each — otherwise a caller can double its own
    /// allowance simply by connecting over a dual-stack socket.
    /// </para>
    /// </summary>
    public static string GetClientPartitionKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return "anonymous";
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        return remoteIp.ToString();
    }
}
