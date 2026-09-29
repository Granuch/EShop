using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EShop.ApiGateway.Notifications;

namespace EShop.ApiGateway.IntegrationTests.Fixtures;

// Not sealed: RouteAuthorizationApiFactory derives from it to raise the global rate limit, which
// has to happen through UseSetting rather than the ConfigureAppConfiguration block below.
public class GatewayApiFactory : WebApplicationFactory<Program>
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
                // Notices exist only when an operator is subscribed (frontend-contracts F-55).
                ["Gateway:OperationsEmailRecipients:0"] = "ops@test.local",
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

            // Drop the real "downstream" check. The clusters configured above point at
            // 127.0.0.1:65000, which is deliberately unreachable so the proxy-failure tests can
            // exercise the 502->503 path, so the real check would be Unhealthy and slow every
            // /health call. It has not been on /health/ready since docker-ci DC-45, so readiness
            // does not need this; SystemEndpointsTests does, because it registers a counting
            // probe under the same name and a duplicate name throws. Downstream reachability is
            // covered by DownstreamHealthCheckTests in the unit-test project, and the real
            // registrations by Health/GatewayReadinessScopeTests, which does not use this fixture.
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
