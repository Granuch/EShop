using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// H3. Turns Catalog's rate limiters on under Testing, lowers the limits to something a test can
/// exhaust, trusts a simulated gateway so <c>X-Forwarded-For</c> is honoured, and lets each request
/// choose the client address it presents.
///
/// <para>
/// Ported from Identity's factory of the same name. Without it Catalog's limiters are unreachable
/// from any test — they are skipped outright in the Testing environment — so the partition key,
/// which is the part H3 was actually about, could not be verified at all.
/// </para>
/// </summary>
public class RateLimitingApiFactory : PostgresCatalogApiFactory
{
    /// <summary>
    /// Request header standing in for the TCP peer address. TestServer has no socket, so
    /// <c>HttpContext.Connection.RemoteIpAddress</c> is null unless something sets it — which is
    /// why every ordinary test request shares the "anonymous" partition.
    /// </summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    /// <summary>Peer address the app is configured to trust as a reverse proxy.</summary>
    public const string TrustedProxyIp = "10.99.0.1";

    public const int GlobalPermitLimit = 5;
    public const int SearchPermitLimit = 3;

    private RateLimitingApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static async Task<RateLimitingApiFactory> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync(cancellationToken);
        return new RateLimitingApiFactory(connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, not ConfigureAppConfiguration: Program.cs reads these while composing the
        // app, and a ConfigureAppConfiguration source is only applied when the host is finally
        // built — after AddRateLimiter has already captured the limits.
        builder.UseSetting("RateLimiting:EnableInTesting", "true");
        builder.UseSetting("RateLimiting:Global:PermitLimit", GlobalPermitLimit.ToString());
        builder.UseSetting("RateLimiting:Global:WindowSeconds", "60");
        builder.UseSetting("RateLimiting:Search:PermitLimit", SearchPermitLimit.ToString());
        builder.UseSetting("RateLimiting:Search:WindowSeconds", "60");

        // Trust the simulated gateway so UseForwardedHeaders rewrites RemoteIpAddress from
        // X-Forwarded-For before the rate limiter partitions on it. This is the setting
        // docker-compose.yml was missing, which is what made every client share one bucket.
        builder.UseSetting("ForwardedHeaders:KnownProxies:0", TrustedProxyIp);

        base.ConfigureWebHost(builder);
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.AddTransient<IStartupFilter, RemoteIpTestStartupFilter>();
    }
}

/// <summary>
/// Sets <c>Connection.RemoteIpAddress</c> from <see cref="RateLimitingApiFactory.RemoteIpHeader"/>
/// so tests can present distinct client addresses. Startup filters run ahead of the application's
/// own pipeline, so this lands before UseForwardedHeaders and UseRateLimiter.
/// </summary>
internal sealed class RemoteIpTestStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RateLimitingApiFactory.RemoteIpHeader, out var header)
                    && IPAddress.TryParse(header.ToString(), out var remoteIp))
                {
                    context.Connection.RemoteIpAddress = remoteIp;
                }

                await nextMiddleware();
            });

            next(app);
        };
    }
}
