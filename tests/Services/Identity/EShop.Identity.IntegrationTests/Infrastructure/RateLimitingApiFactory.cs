using EShop.Identity.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Custom factory for rate limiting tests
/// Note: Rate limiting configuration in WebApplicationFactory is complex
/// as AddRateLimiter is typically called on WebApplicationBuilder, not IServiceCollection.
/// For now, this factory serves as a placeholder. Rate limiting tests may need
/// to be run against a real deployment or use a different approach.
/// </summary>
public class RateLimitingApiFactory : IdentityApiFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        // TODO: Configure rate limiting for tests
        // This would typically require builder.Services.AddRateLimiter() 
        // which is not directly accessible from ConfigureServices
        // Consider alternative approaches:
        // 1. Custom middleware for testing
        // 2. Test against deployed service
        // 3. Use a different WebApplicationFactory configuration approach
    }
}
