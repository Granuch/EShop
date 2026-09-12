using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Fixtures;

/// <summary>
/// Ordering audit L9, ported from Catalog's factory of the same name. Turns Ordering's rate limiter on
/// under Testing, lowers the limit to something a test can exhaust, trusts a simulated gateway so
/// <c>X-Forwarded-For</c> is honoured, and lets each request choose the client address it presents.
/// Without it the limiter is unreachable from any test: it is skipped outright under Testing.
/// </summary>
public sealed class RateLimitingApiFactory : PostgresOrderingApiFactory
{
    /// <summary>Stands in for the TCP peer address; TestServer has no socket.</summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    /// <summary>Peer address the app is configured to trust as the gateway.</summary>
    public const string TrustedProxyIp = "10.99.0.1";

    public const int GlobalPermitLimit = 5;

    private RateLimitingApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static new async Task<RateLimitingApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting: Program.cs reads these while composing the app, before AddRateLimiter captures them.
        builder.UseSetting("RateLimiting:EnableInTesting", "true");
        builder.UseSetting("RateLimiting:Global:PermitLimit", GlobalPermitLimit.ToString());
        builder.UseSetting("RateLimiting:Global:WindowSeconds", "60");
        builder.UseSetting("ForwardedHeaders:KnownProxies:0", TrustedProxyIp);

        base.ConfigureWebHost(builder);
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.AddTransient<IStartupFilter, RemoteIpTestStartupFilter>();
    }

    /// <summary>
    /// Sets <c>Connection.RemoteIpAddress</c> from <see cref="RemoteIpHeader"/>. Startup filters run
    /// ahead of the application's pipeline, so this lands before UseForwardedHeaders and UseRateLimiter.
    /// </summary>
    private sealed class RemoteIpTestStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var header)
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
