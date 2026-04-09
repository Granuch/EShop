using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EShop.ApiGateway.Notifications;

namespace EShop.ApiGateway.IntegrationTests.Fixtures;

public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    public TestNotificationCollector NotificationCollector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!",
                ["JwtSettings:Issuer"] = "EShop.Identity",
                ["JwtSettings:Audience"] = "EShop.Services",

                ["Gateway:EnableAuditEmailNotifications"] = "true",
                ["Gateway:EnableSimulationFailureEmailNotifications"] = "true",
                ["Gateway:EnableRateLimitEmailNotifications"] = "true",
                ["RateLimiting:GlobalPermitLimit"] = "1",
                ["RateLimiting:GlobalWindowSeconds"] = "60",

                ["ReverseProxy:Routes:test-orders:ClusterId"] = "test-orders-cluster",
                ["ReverseProxy:Routes:test-orders:Match:Path"] = "/test/orders/{**catch-all}",
                ["ReverseProxy:Clusters:test-orders-cluster:Destinations:d1:Address"] = "http://127.0.0.1:65000/",

                ["ReverseProxy:Routes:test-failure:ClusterId"] = "test-orders-cluster",
                ["ReverseProxy:Routes:test-failure:Match:Path"] = "/test/failure/{**catch-all}",

                ["Simulation:Enabled"] = "true",
                ["Simulation:AllowHeaderOverride"] = "true",
                ["Simulation:Routes:test-orders:PathPrefix"] = "/test/orders",
                ["Simulation:Routes:test-orders:DelayMs:Min"] = "0",
                ["Simulation:Routes:test-orders:DelayMs:Max"] = "0",
                ["Simulation:Routes:test-orders:ErrorRate"] = "0",
                ["Simulation:Routes:test-orders:ResponseTemplate"] = "orders_list",

                ["Simulation:Routes:test-failure:PathPrefix"] = "/test/failure",
                ["Simulation:Routes:test-failure:DelayMs:Min"] = "0",
                ["Simulation:Routes:test-failure:DelayMs:Max"] = "0",
                ["Simulation:Routes:test-failure:ErrorRate"] = "0",
                ["Simulation:Routes:test-failure:ForcedFailureMode"] = "503",
                ["Simulation:Routes:test-failure:ResponseTemplate"] = "default",

                ["Email:Host"] = "",
                ["EmailQueueHealth:BacklogWarningThreshold"] = "100",
                ["EmailQueueHealth:BacklogUnhealthyThreshold"] = "500",
                ["EmailQueueHealth:DroppedWarningThreshold"] = "1",
                ["EmailQueueHealth:DroppedUnhealthyThreshold"] = "10"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailNotificationService>();
            services.AddSingleton<IEmailNotificationService>(NotificationCollector);
        });
    }
}
