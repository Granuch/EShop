using System.Net;
using EShop.Identity.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Custom factory for rate limiting tests.
/// Enables rate limiting in Testing environment, configures a trusted proxy so
/// X-Forwarded-For is honoured, and lets each test choose the client IP it presents.
/// </summary>
public class RateLimitingApiFactory : IdentityApiFactory
{
    /// <summary>
    /// Request header standing in for the TCP peer address. TestServer has no socket, so
    /// <c>HttpContext.Connection.RemoteIpAddress</c> is null unless something sets it.
    /// </summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    /// <summary>
    /// Peer address the app is configured to trust as a reverse proxy.
    /// </summary>
    public const string TrustedProxyIp = "10.99.0.1";

    public const int LoginPermitLimit = 2;
    public const int AuthPermitLimit = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, not ConfigureAppConfiguration. Program.cs reads these values in its
        // top-level statements while composing the app; sources added through
        // ConfigureAppConfiguration are only applied when the host is finally built, which is
        // after AddRateLimiter has already captured the limits. Delivering them as host
        // configuration is what makes them visible in time.
        builder.UseSetting("RateLimiting:EnableInTesting", "true");
        builder.UseSetting("RateLimiting:Global:PermitLimit", "100");
        builder.UseSetting("RateLimiting:Global:WindowSeconds", "60");
        builder.UseSetting("RateLimiting:Auth:PermitLimit", AuthPermitLimit.ToString());
        builder.UseSetting("RateLimiting:Auth:WindowSeconds", "60");
        builder.UseSetting("RateLimiting:Login:PermitLimit", LoginPermitLimit.ToString());
        builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");

        // Trust the simulated gateway so UseForwardedHeaders rewrites RemoteIpAddress from
        // X-Forwarded-For before the rate limiter partitions on it.
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
/// so tests can present distinct client addresses. Startup filters run ahead of the
/// application's own pipeline, so this lands before UseForwardedHeaders and UseRateLimiter.
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
