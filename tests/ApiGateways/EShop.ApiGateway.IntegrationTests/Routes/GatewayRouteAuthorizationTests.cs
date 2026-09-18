using System.Net;
using System.Net.Http.Headers;
using EShop.ApiGateway.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace EShop.ApiGateway.IntegrationTests.Routes;

/// <summary>
/// The gateway's half of "defense in depth", which until now nothing tested at any layer.
///
/// <para>
/// The gateway declares no <c>FallbackPolicy</c>, so a YARP route added to <c>appsettings.json</c>
/// without an <c>AuthorizationPolicy</c> is <b>anonymous</b> and nothing goes red. That is the
/// failure this fixture exists to make impossible: <see cref="EveryApiRoute_IsListedHere_WithItsPolicy"/>
/// pins the whole routing table, so adding an admin route without a decision fails the build.
/// </para>
///
/// <para>
/// The behavioural half reads status <i>classes</i>, not exact codes, for authorized requests: the
/// real clusters address <c>http://identity-api:8080/</c> and friends, which do not resolve from a
/// test host, so an authorized request proxies and comes back 502 or 503. That is the signal —
/// "the gateway let it through" — and it is exactly what distinguishes an authorization pass from
/// a 401/403.
/// </para>
/// </summary>
[TestFixture]
public sealed class GatewayRouteAuthorizationTests
{
    private RouteAuthorizationApiFactory _factory = null!;
    private HttpClient _client = null!;

    /// <summary>
    /// Every route the gateway serves under <c>/api/</c>, with the policy it must carry.
    /// <c>null</c> means deliberately anonymous. A route added or removed without updating this
    /// table fails <see cref="EveryApiRoute_IsListedHere_WithItsPolicy"/>.
    /// </summary>
    private static readonly Dictionary<string, string?> ExpectedPolicies = new(StringComparer.Ordinal)
    {
        ["identity-route"] = null,
        ["identity-account-route"] = "Authenticated",
        ["identity-roles-route"] = "Admin",
        ["catalog-products-write-route"] = "Admin",
        ["catalog-products-read-route"] = null,
        ["catalog-categories-write-route"] = "Admin",
        ["catalog-categories-read-route"] = null,
        ["basket-route"] = "Authenticated",
        ["orders-route"] = "Authenticated",
        ["payments-route"] = "Authenticated",
        ["ordering-user-orders-route"] = "Authenticated",
        ["payment-user-payments-route"] = "Authenticated"
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new RouteAuthorizationApiFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<HttpStatusCode> Send(HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);

        // A second client per token would inherit DefaultRequestHeaders; setting the header on the
        // request itself is only safe because this client never sets a default one.
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    private static void AssertReachedProxy(HttpStatusCode status, string what)
        => Assert.That(
            status,
            Is.Not.EqualTo(HttpStatusCode.Unauthorized).And.Not.EqualTo(HttpStatusCode.Forbidden),
            $"{what} must pass the gateway's authorization and reach the proxy (a 502/503 from the "
            + "unreachable test destination is the expected outcome)");

    // ---------- structural ----------

    [Test]
    public void EveryApiRoute_IsListedHere_WithItsPolicy()
    {
        var routes = _factory.Services.GetRequiredService<IProxyConfigProvider>()
            .GetConfig().Routes
            .Where(r => r.Match.Path?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToDictionary(r => r.RouteId, r => r.AuthorizationPolicy, StringComparer.Ordinal);

        Assert.That(routes.Keys, Is.EquivalentTo(ExpectedPolicies.Keys),
            "a gateway route under /api/ was added or removed — list it above with the policy it must carry, "
            + "because a route with no AuthorizationPolicy is anonymous and there is no FallbackPolicy to catch it");

        foreach (var (routeId, expected) in ExpectedPolicies)
        {
            Assert.That(routes[routeId], Is.EqualTo(expected), $"route '{routeId}' carries the wrong policy");
        }
    }

    [Test]
    public void EveryDeclaredPolicy_IsOneTheGatewayActuallyRegisters()
    {
        // A policy name that is not registered throws at request time, not at startup — a typo in
        // appsettings.json would therefore surface as a 500 on the first admin request in production.
        var declared = _factory.Services.GetRequiredService<IProxyConfigProvider>()
            .GetConfig().Routes
            .Select(r => r.AuthorizationPolicy)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.Ordinal);

        Assert.That(declared, Is.SubsetOf(new[] { "Authenticated", "Admin" }));
    }

    // ---------- behavioural ----------

    [Test]
    public async Task AnAdminRoute_RefusesAnonymous_WithUnauthorized()
    {
        Assert.That(await Send(HttpMethod.Get, "/api/v1/roles", token: null),
            Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AnAdminRoute_RefusesASignedInNonAdmin_WithForbidden()
    {
        // The half that matters: a suite that only ever signs in as Admin cannot tell
        // RequireRole("Admin") from RequireAuthenticatedUser().
        Assert.That(await Send(HttpMethod.Get, "/api/v1/roles", RouteAuthorizationApiFactory.UserToken()),
            Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AnAdminRoute_AdmitsAnAdmin()
    {
        AssertReachedProxy(
            await Send(HttpMethod.Get, "/api/v1/roles", RouteAuthorizationApiFactory.AdminToken()),
            "an admin calling /api/v1/roles");
    }

    [Test]
    public async Task CatalogWrites_AreAdminOnly_WhileCatalogReadsAreAnonymous()
    {
        Assert.That(await Send(HttpMethod.Post, "/api/v1/products", token: null),
            Is.EqualTo(HttpStatusCode.Unauthorized), "POST /products is an admin write");
        Assert.That(await Send(HttpMethod.Post, "/api/v1/products", RouteAuthorizationApiFactory.UserToken()),
            Is.EqualTo(HttpStatusCode.Forbidden), "POST /products is an admin write");

        AssertReachedProxy(
            await Send(HttpMethod.Get, "/api/v1/products", token: null),
            "an anonymous GET /api/v1/products");
    }

    [Test]
    public async Task CategoryWrites_AreAdminOnly_WhileCategoryReadsAreAnonymous()
    {
        Assert.That(await Send(HttpMethod.Delete, "/api/v1/categories/x", RouteAuthorizationApiFactory.UserToken()),
            Is.EqualTo(HttpStatusCode.Forbidden));

        AssertReachedProxy(
            await Send(HttpMethod.Get, "/api/v1/categories", token: null),
            "an anonymous GET /api/v1/categories");
    }

    [Test]
    public async Task AuthenticatedRoutes_RefuseAnonymous_AndAdmitAnyUser()
    {
        foreach (var path in new[]
                 {
                     "/api/v1/account/profile",
                     "/api/v1/basket/user-1",
                     "/api/v1/orders",
                     "/api/v1/payments/00000000-0000-0000-0000-000000000001",
                     "/api/v1/users/user-1/orders",
                     "/api/v1/users/user-1/payments"
                 })
        {
            Assert.That(await Send(HttpMethod.Get, path, token: null),
                Is.EqualTo(HttpStatusCode.Unauthorized), path);

            AssertReachedProxy(
                await Send(HttpMethod.Get, path, RouteAuthorizationApiFactory.UserToken()),
                $"a signed-in user calling {path}");
        }
    }

    [Test]
    public async Task TheAuthRoute_IsAnonymous()
    {
        // Login and registration must stay anonymous — the gateway declaring a FallbackPolicy later
        // would otherwise lock every user out of the platform.
        AssertReachedProxy(
            await Send(HttpMethod.Post, "/api/v1/auth/login", token: null),
            "an anonymous POST /api/v1/auth/login");
    }

    [Test]
    public async Task AnUnroutedAdminPath_Is404_NotAnonymouslyProxied()
    {
        // Until an /api/v1/admin/** route exists, the safe answer is "no such route". This pins the
        // current state so the stage that adds admin routes has to add them deliberately.
        Assert.That(await Send(HttpMethod.Get, "/api/v1/admin/users", RouteAuthorizationApiFactory.AdminToken()),
            Is.EqualTo(HttpStatusCode.NotFound));
    }
}
