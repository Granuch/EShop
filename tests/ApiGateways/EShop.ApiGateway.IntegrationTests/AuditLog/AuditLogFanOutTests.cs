using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EShop.ApiGateway.AuditLog;
using EShop.ApiGateway.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.IntegrationTests.AuditLog;

/// <summary>
/// Admin panel S15. The gateway's own <c>GET /api/v1/admin/audit</c>: it authorizes <c>audit.read</c>, asks every
/// audited service for a page with the caller's token, filters and correlation id, and merges the answers.
///
/// <para>
/// The services are faked at the HTTP boundary — the fan-out's named client gets a handler that serves an in-memory
/// trail per cluster host, the way each service's <c>AuditLogReader</c> does — so what is asserted is exactly what the
/// gateway sends and how it merges what comes back.
/// </para>
/// </summary>
[TestFixture]
public sealed class AuditLogFanOutTests
{
    private const string Path = "/api/v1/admin/audit";

    private FanOutApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new FanOutApiFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string? token = null, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? RouteAuthorizationApiFactory.AdminToken());
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return await _client.SendAsync(request);
    }

    private async Task<GatewayAuditLogPageDto> PageAsync(string url)
    {
        using var response = await GetAsync(url);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GatewayAuditLogPageDto>())!;
    }

    // ---------- authorization ----------

    [Test]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        using var anonymous = _factory.CreateClient();
        Assert.That((await anonymous.GetAsync(Path)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ACustomer_IsForbidden_AndNoServiceIsAsked()
    {
        Assert.That((await GetAsync(Path, RouteAuthorizationApiFactory.UserToken())).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_factory.Services_.Requests, Is.Empty, "a refused caller must not cost five downstream calls");
    }

    [Test]
    public async Task TheAuditReadPermission_IsEnough_WithoutTheAdminRole()
        => Assert.That((await GetAsync(Path, FanOutApiFactory.PermissionToken(EShopPermissions.AuditRead))).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

    [Test]
    public async Task AnotherPermission_IsNot()
        => Assert.That((await GetAsync(Path, FanOutApiFactory.PermissionToken(EShopPermissions.SystemManage))).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

    [Test]
    public void TheEndpoint_DeclaresTheAuditReadPolicy()
    {
        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == Path);

        Assert.That(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy),
            Does.Contain(EShopPermissions.AuditRead));
    }

    // ---------- fan-out and merge ----------

    [Test]
    public async Task AWalk_ReturnsEveryRowOfEveryService_ExactlyOnce_NewestFirstOnEachPage()
    {
        _factory.Services_.Seed("catalog", 7);
        _factory.Services_.Seed("identity", 4);
        _factory.Services_.Seed("ordering", 3);
        _factory.Services_.Seed("payment", 2);
        _factory.Services_.Seed("notification", 1);

        var seen = new List<AuditLogEntryDto>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await PageAsync(cursor is null ? $"{Path}?pageSize=4" : $"{Path}?pageSize=4&cursor={cursor}");
            Assert.That(page.Items, Has.Count.LessThanOrEqualTo(4));
            Assert.That(page.Items.Select(i => i.OccurredAt), Is.Ordered.Descending);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 20);

        Assert.Multiple(() =>
        {
            Assert.That(cursor, Is.Null, "the walk ends");
            Assert.That(seen.Select(s => $"{s.Service}:{s.Id}"), Is.Unique);
            Assert.That(seen, Has.Count.EqualTo(17));
            Assert.That(seen.Select(s => s.Service).Distinct(),
                Is.EquivalentTo(new[] { "catalog", "identity", "ordering", "payment", "notification" }));
        });
    }

    [Test]
    public async Task EachService_IsAskedWithTheCallersToken_CorrelationIdAndFilters()
    {
        var token = RouteAuthorizationApiFactory.AdminToken();
        using var response = await GetAsync(
            $"{Path}?entityType=Product&entityId=p-1&outcome=failed&from=2026-09-01&pageSize=7", token, "corr-fan-out");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var requests = _factory.Services_.Requests.ToList();
        Assert.That(requests.Select(r => r.Service), Is.EquivalentTo(AuditLogFanOut.Sources.Keys));
        Assert.Multiple(() =>
        {
            foreach (var r in requests)
            {
                Assert.That(r.Authorization, Is.EqualTo($"Bearer {token}"), $"{r.Service}: the caller's own token");
                Assert.That(r.CorrelationId, Is.EqualTo("corr-fan-out"), r.Service);
                Assert.That(r.Path, Is.EqualTo(Path), r.Service);
                Assert.That(r.Query["entityType"], Is.EqualTo("Product"), r.Service);
                Assert.That(r.Query["entityId"], Is.EqualTo("p-1"), r.Service);
                Assert.That(r.Query["outcome"], Is.EqualTo("Failed"), r.Service);
                Assert.That(r.Query["pageSize"], Is.EqualTo("7"), r.Service);
                Assert.That(DateTime.Parse(r.Query["from"]!).ToUniversalTime(),
                    Is.EqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)), r.Service);
            }
        });
    }

    [Test]
    public async Task NamingAService_AsksOnlyThatService()
    {
        _factory.Services_.Seed("catalog", 2);
        _factory.Services_.Seed("identity", 2);

        var page = await PageAsync($"{Path}?service=catalog");

        Assert.Multiple(() =>
        {
            Assert.That(_factory.Services_.Requests.Select(r => r.Service), Is.EqualTo(new[] { "catalog" }));
            Assert.That(page.Items.Select(i => i.Service), Is.All.EqualTo("catalog"));
            Assert.That(page.NextCursor, Is.Null);
        });
    }

    [Test]
    public async Task AFailingService_IsNamed_TheOthersAreServed_AndItsRowsArriveOnceItRecovers()
    {
        _factory.Services_.Seed("catalog", 2);
        _factory.Services_.Seed("payment", 2);
        _factory.Services_.Failing.Add("payment");

        var first = await PageAsync($"{Path}?pageSize=10");

        Assert.Multiple(() =>
        {
            Assert.That(first.UnavailableServices, Is.EqualTo(new[] { "payment" }));
            Assert.That(first.Items.Select(i => i.Service), Is.EqualTo(new[] { "catalog", "catalog" }));
            Assert.That(first.NextCursor, Is.Not.Null, "payment is still owed, so the walk is not over");
        });

        _factory.Services_.Failing.Clear();
        var second = await PageAsync($"{Path}?pageSize=10&cursor={first.NextCursor}");

        Assert.Multiple(() =>
        {
            Assert.That(second.UnavailableServices, Is.Empty);
            Assert.That(second.Items.Select(i => i.Service), Is.EqualTo(new[] { "payment", "payment" }),
                "catalog was finished on the first page and is not asked again; payment's rows were not lost");
            Assert.That(second.NextCursor, Is.Null);
        });
    }

    [TestCase("?cursor=not-a-cursor")]
    [TestCase("?service=basket")]
    [TestCase("?pageSize=0")]
    [TestCase("?outcome=7")]
    public async Task AnInvalidQuery_IsA400_AndNoServiceIsAsked(string query)
    {
        using var response = await GetAsync(Path + query);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.RootElement.GetProperty("errorCode").GetString(), Is.EqualTo("Validation.Failed"));
        Assert.That(_factory.Services_.Requests, Is.Empty);
    }

    [Test]
    public void EverySource_NamesAClusterTheGatewayActuallyHas()
    {
        using var real = new RouteAuthorizationApiFactory();
        var clusters = real.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Clusters
            .Select(c => c.ClusterId)
            .ToHashSet();

        Assert.That(AuditLogFanOut.Sources.Values, Is.SubsetOf(clusters),
            "a renamed cluster would silently drop that service from every merged page");
    }

    // ---------- fixtures ----------

    private sealed record SeenRequest(
        string Service, string Path, IReadOnlyDictionary<string, string?> Query, string? Authorization, string? CorrelationId);

    /// <summary>The five audited services, faked at the HTTP boundary by cluster host.</summary>
    private sealed class FakeServices
    {
        private static readonly IReadOnlyDictionary<string, string> Hosts = new Dictionary<string, string>
        {
            ["catalog-api"] = "catalog",
            ["identity-api"] = "identity",
            ["ordering-api"] = "ordering",
            ["payment-api"] = "payment",
            ["notification-api"] = "notification"
        };

        private readonly ConcurrentDictionary<string, List<AuditLogEntryDto>> _trails = new();

        public ConcurrentQueue<SeenRequest> Requests { get; } = new();
        public HashSet<string> Failing { get; } = [];

        public void Seed(string service, int count)
        {
            // Minutes interleave across services so a correct merge has to alternate between them.
            var offset = Hosts.Values.OrderBy(v => v).ToList().IndexOf(service);
            _trails[service] = Enumerable.Range(1, count)
                .Select(i => new AuditLogEntryDto(i, new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 5 + offset),
                    service, "Act", "Thing", $"{service}-{i}", "admin-1", null, null, "Succeeded", null, "{}"))
                .ToList();
        }

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var service = Hosts[request.RequestUri!.Host];
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query)
                .ToDictionary(p => p.Key, p => (string?)p.Value.ToString());
            Requests.Enqueue(new SeenRequest(
                service,
                request.RequestUri.AbsolutePath,
                query,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Correlation-ID", out var c) ? c.Single() : null));

            if (Failing.Contains(service))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            var before = query.TryGetValue("before", out var b) ? long.Parse(b!) : long.MaxValue;
            var pageSize = int.Parse(query["pageSize"]!);
            var remaining = _trails.GetValueOrDefault(service, []).Where(e => e.Id < before).OrderByDescending(e => e.Id).ToList();
            var page = remaining.Take(pageSize).ToList();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new AuditLogPageDto(page, remaining.Count > pageSize ? page[^1].Id : null),
                    options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
        }
    }

    private sealed class FakeServicesHandler(FakeServices services) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(services.Answer(request));
    }

    private sealed class FanOutApiFactory : GatewayApiFactory
    {
        public FakeServices Services_ { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // As RouteAuthorizationApiFactory: the base fixture allows one request a minute, and these tests send several.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:GlobalPermitLimit"] = "100000",
                ["RateLimiting:GlobalWindowSeconds"] = "60"
            }));

            builder.ConfigureServices(services =>
                services.AddHttpClient(AuditLogFanOut.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeServicesHandler(Services_)));
        }

        /// <summary>A token carrying one permission claim and no role.</summary>
        public static string PermissionToken(string permission)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, "operator-1"),
                new(ClaimTypes.NameIdentifier, "operator-1"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(EShopPermissions.ClaimType, permission)
            };

            var token = new JwtSecurityToken(
                issuer: RouteAuthorizationApiFactory.Issuer,
                audience: RouteAuthorizationApiFactory.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RouteAuthorizationApiFactory.SecretKey)),
                    SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
