using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EShop.ApiGateway.Notifications;

namespace EShop.ApiGateway.IntegrationTests.Fixtures;

public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    public TestNotificationCollector NotificationCollector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // JWT settings must go through UseSetting, not ConfigureAppConfiguration. Program.cs
        // reads JwtSettings in its top-level statements while composing the app and throws if
        // SecretKey is blank; sources added via ConfigureAppConfiguration are only applied when
        // the host is finally built, which is after that read. It appeared to work locally only
        // because a developer shell exported JwtSettings__SecretKey — on a clean checkout (and
        // in CI) the guard fired and every test in this assembly failed at SetUp.
        builder.UseSetting("JwtSettings:SecretKey", "TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!");
        builder.UseSetting("JwtSettings:Issuer", "EShop.Identity");
        builder.UseSetting("JwtSettings:Audience", "EShop.Services");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
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

            // Drop the "downstream" readiness check. The clusters configured above point at
            // 127.0.0.1:65000, which is deliberately unreachable so the proxy-failure tests can
            // exercise the 502->503 path — but that also makes DownstreamHealthCheck report
            // Unhealthy, which would turn /health/ready into a 503 for every test in this
            // assembly. Downstream reachability is already covered directly by
            // DownstreamHealthCheckTests in the unit-test project; what the readiness endpoint
            // test needs to prove is that /health/ready aggregates the "ready"-tagged checks and
            // maps a non-Unhealthy aggregate to 200. The remaining ready checks still do that:
            // "smtp" is Degraded (Email:Host is blank above) and "email-queue" is Healthy.
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                var downstream = options.Registrations.FirstOrDefault(r => r.Name == "downstream");
                if (downstream is not null)
                {
                    options.Registrations.Remove(downstream);
                }
            });
        });
    }
}
