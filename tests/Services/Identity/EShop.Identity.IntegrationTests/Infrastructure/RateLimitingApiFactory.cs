using EShop.Identity.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Custom factory for rate limiting tests.
/// Enables rate limiting in Testing environment through configuration override.
/// </summary>
public class RateLimitingApiFactory : IdentityApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:EnableInTesting"] = "true",
                ["RateLimiting:Global:PermitLimit"] = "3",
                ["RateLimiting:Global:WindowSeconds"] = "60",
                ["RateLimiting:Auth:PermitLimit"] = "2",
                ["RateLimiting:Auth:WindowSeconds"] = "60",
                ["RateLimiting:Login:PermitLimit"] = "2",
                ["RateLimiting:Login:WindowSeconds"] = "60"
            });
        });

        base.ConfigureWebHost(builder);
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
    }
}
