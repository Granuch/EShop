using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EShop.ApiGateway.Health;
using EShop.ApiGateway.IntegrationTests.Fixtures;
using EShop.ApiGateway.SystemAdmin;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.SystemAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.IntegrationTests.SystemAdmin;

/// <summary>
/// Admin panel S19. The System page's three gateway-served endpoints — aggregate health (#87), read-only settings (#85)
/// and read-only feature flags (#89) — with the six services faked at the HTTP boundary on the fan-out's named client,
/// as <c>AuditLogFanOutTests</c> fakes the audited ones. What is asserted is what the gateway sends, and what it makes
/// of what comes back.
/// </summary>
[TestFixture]
public sealed class SystemEndpointsTests
{
    private static readonly string[] Paths = [SystemAdminPaths.Health, SystemAdminPaths.Settings, SystemAdminPaths.FeatureFlags];

    private SystemApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new SystemApiFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? token = null, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? RouteAuthorizationApiFactory.AdminToken());
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return await _client.SendAsync(request);
    }

    private async Task<T> ReadAsync<T>(string path)
    {
        using var response = await GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    // ---------- authorization ----------

    [TestCaseSource(nameof(Paths))]
    public async Task AnAnonymousCaller_IsUnauthorized(string path)
    {
        using var anonymous = _factory.CreateClient();
        Assert.That((await anonymous.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCaseSource(nameof(Paths))]
    public async Task ACustomer_IsForbidden_AndNoServiceIsAsked(string path)
    {
        Assert.That((await GetAsync(path, RouteAuthorizationApiFactory.UserToken())).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_factory.Services_.Requests, Is.Empty, "a refused caller must not cost a single downstream call");
    }

    [TestCaseSource(nameof(Paths))]
    public async Task TheSystemManagePermission_IsEnough_WithoutTheAdminRole(string path)
        => Assert.That((await GetAsync(path, RouteAuthorizationApiFactory.PermissionToken(EShopPermissions.SystemManage))).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

    [TestCaseSource(nameof(Paths))]
    public async Task AnotherPermission_IsNot(string path)
        => Assert.That((await GetAsync(path, RouteAuthorizationApiFactory.PermissionToken(EShopPermissions.AuditRead))).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

    [TestCaseSource(nameof(Paths))]
    public void TheEndpoint_DeclaresSystemManage_AndIsNotAnonymous(string path)
    {
        // These are endpoints, not YARP routes, so GatewayRouteAuthorizationTests cannot see them.
        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == path);

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy),
                Does.Contain(EShopPermissions.SystemManage));
            Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null);
        });
    }

    // ---------- health ----------

    [Test]
    public async Task Health_ListsTheGatewayThenEveryService_AndIsTheWorstOfThem()
    {
        var health = await ReadAsync<SystemHealthDto>(SystemAdminPaths.Health);

        Assert.Multiple(() =>
        {
            Assert.That(health.Components.Select(c => c.Name),
                Is.EqualTo(new[] { "gateway" }.Concat(SystemFanOut.Sources.Select(s => s.Name))));
            Assert.That(health.Components.Skip(1).Select(c => (c.Reachable, c.Status)),
                Is.All.EqualTo((true, "Healthy")));
            // The fixture blanks Email:Host, so the gateway's own smtp check is Degraded — and so is the whole.
            Assert.That(health.Components[0].Status, Is.EqualTo("Degraded"));
            Assert.That(health.Status, Is.EqualTo("Degraded"));
            Assert.That(health.Components.Single(c => c.Name == "catalog").Checks.Select(c => c.Name),
                Is.EqualTo(new[] { "database", "redis" }));
        });
    }

    [Test]
    public async Task Health_DoesNotRunTheGatewaysDownstreamCheck_WhichWouldProbeEveryServiceAgain()
    {
        var health = await ReadAsync<SystemHealthDto>(SystemAdminPaths.Health);

        Assert.Multiple(() =>
        {
            Assert.That(health.Components[0].Checks.Select(c => c.Name), Does.Not.Contain(DownstreamHealthCheck.Name));
            Assert.That(health.Components[0].Checks.Select(c => c.Name), Does.Contain("gateway-liveness"));
            Assert.That(_factory.DownstreamProbeRuns, Is.Zero);
        });
    }

    [Test]
    public async Task Health_ReadsEachServiceAnonymously()
    {
        await ReadAsync<SystemHealthDto>(SystemAdminPaths.Health);

        var probes = _factory.Services_.Requests.Where(r => r.Path == "/health").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(probes.Select(p => p.Service), Is.EquivalentTo(SystemFanOut.Sources.Select(s => s.Name)));
            Assert.That(probes.Select(p => p.Authorization), Is.All.Null, "the administrator's token has no business on /health");
        });
    }

    [Test]
    public async Task Health_AServiceAnswering503_IsReportedUnhealthy_WithItsChecks_AndTheAnswerIsStill200()
    {
        _factory.Services_.Health["payment"] = (HttpStatusCode.ServiceUnavailable,
            """{"status":"Unhealthy","totalDurationMs":3.1,"checks":[{"name":"database","status":"Unhealthy"},{"name":"rabbitmq","status":"Healthy"}]}""");

        var health = await ReadAsync<SystemHealthDto>(SystemAdminPaths.Health);
        var payment = health.Components.Single(c => c.Name == "payment");

        Assert.Multiple(() =>
        {
            Assert.That(health.Status, Is.EqualTo("Unhealthy"));
            Assert.That((payment.Reachable, payment.Status), Is.EqualTo((true, "Unhealthy")));
            Assert.That(payment.Checks.Select(c => $"{c.Name}={c.Status}"), Is.EqualTo(new[] { "database=Unhealthy", "rabbitmq=Healthy" }));
        });
    }

    [Test]
    public async Task Health_AServiceThatCannotBeReached_IsUnreachableAndUnhealthy_WithoutFailingThePage()
    {
        _factory.Services_.Unreachable.Add("notification");

        var health = await ReadAsync<SystemHealthDto>(SystemAdminPaths.Health);
        var notification = health.Components.Single(c => c.Name == "notification");

        Assert.Multiple(() =>
        {
            Assert.That((notification.Reachable, notification.Status), Is.EqualTo((false, "Unhealthy")));
            Assert.That(notification.Checks, Is.Empty);
            Assert.That(health.Status, Is.EqualTo("Unhealthy"));
        });
    }

    [Test]
    public async Task Health_SomethingAnsweringWithoutAHealthBody_IsReachableButUnhealthy()
    {
        _factory.Services_.Health["basket"] = (HttpStatusCode.NotFound, "<html>Not Found</html>");

        var basket = (await ReadAsync<SystemHealthDto>(SystemAdminPaths.Health)).Components.Single(c => c.Name == "basket");

        Assert.That((basket.Reachable, basket.Status, basket.Checks.Count), Is.EqualTo((true, "Unhealthy", 0)));
    }

    [Test]
    public async Task Health_RepeatsNothingButNamesAndKnownStatuses()
    {
        // SEC-07's line held one level up: a body that carries more than name/status — or a status that is not a
        // HealthStatus name, including a number, which Enum.TryParse would have accepted — is not echoed.
        _factory.Services_.Health["identity"] = (HttpStatusCode.OK,
            """{"status":"Healthy","description":"db-secret-host:5432","checks":[{"name":"database","status":"5","exception":"password=hunter2"}]}""");
        _factory.Services_.Health["ordering"] = (HttpStatusCode.OK, """{"status":"2","checks":[]}""");

        using var response = await GetAsync(SystemAdminPaths.Health);
        var text = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<SystemHealthDto>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("db-secret-host").And.Not.Contain("hunter2").And.Not.Contain("\"5\""));
            Assert.That(health.Components.Single(c => c.Name == "identity").Checks.Single().Status, Is.EqualTo("Unhealthy"));
            Assert.That(health.Components.Single(c => c.Name == "ordering").Status, Is.EqualTo("Unhealthy"));
        });
    }

    // ---------- settings ----------

    [Test]
    public async Task Settings_ComposeOrderingsPricingAndPaymentsProvider_AskedAsTheCaller()
    {
        using var response = await GetAsync(SystemAdminPaths.Settings, correlationId: "settings-correlation");
        var settings = (await response.Content.ReadFromJsonAsync<SystemSettingsDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(settings.Pricing, Is.EqualTo(new PricingSettingsDto("USD", TaxApplied: false, ShippingCharged: false)));
            Assert.That(settings.Payments, Is.EqualTo(new PaymentSettingsDto(PaymentProviders.Simulator)));
            Assert.That(settings.UnavailableServices, Is.Empty);

            var asked = _factory.Services_.Requests.ToList();
            Assert.That(asked.Select(r => (r.Service, r.Path)),
                Is.EquivalentTo(new[] { ("ordering", SystemAdminPaths.Settings), ("payment", SystemAdminPaths.Settings) }));
            Assert.That(asked.Select(r => r.Authorization), Is.All.StartsWith("Bearer "), "each service authorizes the caller itself");
            Assert.That(asked.Select(r => r.CorrelationId), Is.All.EqualTo("settings-correlation"));
        });
    }

    [Test]
    public async Task Settings_AServiceThatCannotBeRead_IsNamed_AndItsSliceIsNull()
    {
        _factory.Services_.Failing.Add("ordering");

        var settings = await ReadAsync<SystemSettingsDto>(SystemAdminPaths.Settings);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Pricing, Is.Null);
            Assert.That(settings.Payments, Is.Not.Null);
            Assert.That(settings.UnavailableServices, Is.EqualTo(new[] { "ordering" }));
        });
    }

    [Test]
    public async Task Settings_AServiceThatCannotBeReached_IsNamed_TooRatherThanFailingThePage()
    {
        // The failing case above answers 500, which never reaches the fan-out's catch. A refused connection does, and
        // falsification round R13 (the catch narrowed to JSON errors) stayed green until this test existed.
        _factory.Services_.Unreachable.Add("payment");

        var settings = await ReadAsync<SystemSettingsDto>(SystemAdminPaths.Settings);

        Assert.Multiple(() =>
        {
            Assert.That(settings.Payments, Is.Null);
            Assert.That(settings.Pricing, Is.Not.Null);
            Assert.That(settings.UnavailableServices, Is.EqualTo(new[] { "payment" }));
        });
    }

    // ---------- feature flags ----------

    [Test]
    public async Task FeatureFlags_ReportTheGatewaysFaultInjection_AsTheMiddlewareWillApplyIt_AndPaymentsSimulator()
    {
        var flags = await ReadAsync<FeatureFlagsDto>(SystemAdminPaths.FeatureFlags);

        var routes = flags.GatewaySimulation.Routes;
        var failure = routes.Single(r => r.RouteId == "test-failure");

        Assert.Multiple(() =>
        {
            // GatewayApiFactory turns simulation on with header override and adds two routes to the ones appsettings.json
            // already declares; the two it adds are the ones whose values this test knows.
            Assert.That((flags.GatewaySimulation.Enabled, flags.GatewaySimulation.AllowHeaderOverride), Is.EqualTo((true, true)));
            Assert.That(routes.Select(r => r.RouteId), Does.Contain("test-orders").And.Contain("test-failure"));
            Assert.That(routes.Select(r => r.RouteId), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(routes.Select(r => r.Active), Is.EqualTo(routes.Select(r => r.Enabled)),
                "with the master switch on, a route is active exactly when its own switch is");
            Assert.That((failure.PathPrefix, failure.ForcedFailureMode, failure.Active), Is.EqualTo(("/test/failure", "503", true)));
            Assert.That(flags.Payments, Is.EqualTo(FakeServices.PaymentFlags));
            Assert.That(flags.UnavailableServices, Is.Empty);
        });
    }

    [Test]
    public async Task FeatureFlags_WithTheMasterSwitchOff_NoRouteIsActive()
    {
        using var off = new SystemApiFactory(simulationEnabled: false);
        using var client = off.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SystemAdminPaths.FeatureFlags);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RouteAuthorizationApiFactory.AdminToken());

        var flags = (await (await client.SendAsync(request)).Content.ReadFromJsonAsync<FeatureFlagsDto>())!;

        Assert.Multiple(() =>
        {
            Assert.That(flags.GatewaySimulation.Enabled, Is.False);
            Assert.That(flags.GatewaySimulation.Routes.Single(r => r.RouteId == "test-failure").Enabled, Is.True,
                "the route's own switch is still on");
            Assert.That(flags.GatewaySimulation.Routes.Select(r => r.Active), Is.All.False);
        });
    }

    [Test]
    public async Task FeatureFlags_PaymentUnreadable_IsNamed_AndTheGatewaysOwnFlagsAreStillThere()
    {
        _factory.Services_.Failing.Add("payment");

        var flags = await ReadAsync<FeatureFlagsDto>(SystemAdminPaths.FeatureFlags);

        Assert.Multiple(() =>
        {
            Assert.That(flags.Payments, Is.Null);
            Assert.That(flags.UnavailableServices, Is.EqualTo(new[] { "payment" }));
            Assert.That(flags.GatewaySimulation.Routes, Is.Not.Empty);
        });
    }

    [Test]
    public async Task ThereIsNoWay_ToChangeAFlagOrASetting()
    {
        // Read-only by decision (Q9c for settings, S19 for flags): no PUT/POST exists to be proxied or served.
        foreach (var path in new[] { SystemAdminPaths.Settings, SystemAdminPaths.FeatureFlags })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", RouteAuthorizationApiFactory.AdminToken());

            Assert.That((await _client.SendAsync(request)).StatusCode,
                Is.EqualTo(HttpStatusCode.MethodNotAllowed).Or.EqualTo(HttpStatusCode.NotFound), path);
        }

        Assert.That(_factory.Services_.Requests, Is.Empty);
    }

    // ---------- the source list ----------

    [Test]
    public void TheSources_AreExactlyTheGatewaysServiceClusters()
    {
        // Both directions: a source naming a missing cluster would read "unreachable" forever, and a cluster with no
        // source would be absent from the health page. The base fixture's own test-* cluster is not a service.
        using var real = new RouteAuthorizationApiFactory();
        var clusters = real.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Clusters
            .Select(c => c.ClusterId)
            .Where(id => !id.StartsWith("test-", StringComparison.Ordinal));

        Assert.That(SystemFanOut.Sources.Select(s => s.ClusterId), Is.EquivalentTo(clusters));
    }

    // ---------- fixtures ----------

    private sealed record SeenRequest(string Service, string Path, string? Authorization, string? CorrelationId);

    /// <summary>The six services, faked at the HTTP boundary by cluster host.</summary>
    private sealed class FakeServices
    {
        public static readonly PaymentFeatureFlagsDto PaymentFlags = new(
            SimulatorActive: true, SimulationMode: "AlwaysSuccess", SuccessRatePercent: 90,
            ProcessingDelayMinSeconds: 1, ProcessingDelayMaxSeconds: 2, RefundDelaySeconds: 3,
            WebhookSignatureVerificationSkipped: false);

        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        public ConcurrentQueue<SeenRequest> Requests { get; } = new();
        public ConcurrentDictionary<string, (HttpStatusCode Status, string Body)> Health { get; } = new();
        public ConcurrentBag<string> Failing { get; } = [];
        public ConcurrentBag<string> Unreachable { get; } = [];

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var service = request.RequestUri!.Host.Replace("-api", string.Empty, StringComparison.Ordinal);
            var path = request.RequestUri.AbsolutePath;
            Requests.Enqueue(new SeenRequest(
                service,
                path,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Correlation-ID", out var c) ? c.Single() : null));

            if (Unreachable.Contains(service))
            {
                throw new HttpRequestException($"{service}-api:8080 refused the connection");
            }

            if (path == "/health")
            {
                var (status, body) = Health.GetValueOrDefault(service, (HttpStatusCode.OK,
                    """{"status":"Healthy","totalDurationMs":1.5,"checks":[{"name":"database","status":"Healthy"},{"name":"redis","status":"Healthy"}]}"""));
                return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }

            if (Failing.Contains(service))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            object? answer = (service, path) switch
            {
                ("ordering", SystemAdminPaths.Settings) => new PricingSettingsDto("USD", false, false),
                ("payment", SystemAdminPaths.Settings) => new PaymentSettingsDto(PaymentProviders.Simulator),
                ("payment", SystemAdminPaths.FeatureFlags) => PaymentFlags,
                _ => null
            };

            return answer is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(answer, answer.GetType(), options: Web) };
        }
    }

    private sealed class FakeServicesHandler(FakeServices services) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(services.Answer(request));
    }

    /// <summary>A health check under the downstream check's name that only counts whether it ran.</summary>
    private sealed class CountingProbe(SystemApiFactory factory) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref factory.DownstreamProbeRuns);
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }
    }

    private sealed class SystemApiFactory(bool simulationEnabled = true) : GatewayApiFactory
    {
        public FakeServices Services_ { get; } = new();

        public int DownstreamProbeRuns;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // As RouteAuthorizationApiFactory: the base fixture allows one request a minute, and these tests send several.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:GlobalPermitLimit"] = "100000",
                ["RateLimiting:GlobalWindowSeconds"] = "60",
                ["Simulation:Enabled"] = simulationEnabled ? "true" : "false"
            }));

            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(SystemFanOut.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeServicesHandler(Services_));

                // The base fixture removes the real downstream check (its clusters are unreachable). Put one back under
                // the same name, counting its runs, so the aggregate's exclusion of it is observable.
                services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Add(
                    new HealthCheckRegistration(DownstreamHealthCheck.Name, _ => new CountingProbe(this), null, ["ready"])));
            });
        }
    }
}
