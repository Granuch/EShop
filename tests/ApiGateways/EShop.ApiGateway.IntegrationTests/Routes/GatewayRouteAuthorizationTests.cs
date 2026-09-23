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
        // G1 (Admin panel S6). Everything under /api/v1/admin is admin-only whatever the method.
        ["admin-users-route"] = "Admin",
        ["catalog-products-write-route"] = "Admin",
        ["catalog-products-read-route"] = null,
        // Admin panel S4. The one admin-only GET under a prefix whose read route is anonymous; it
        // wins only because its Order (19) is lower than the read route's (21).
        ["catalog-products-deleted-route"] = "Admin",
        // Admin panel S16. The CSV export — the second admin-only GET under the anonymous read prefix, winning by the
        // same Order (19 < 21).
        ["catalog-products-export-route"] = "Admin",
        // G2. Everything under /api/v1/admin is admin-only whatever the method.
        ["admin-catalog-route"] = "Admin",
        // Admin panel S19 (#88). Catalog's cache lever. The role question here, system.manage in Catalog — as every YARP
        // admin route does it. The System page's other three paths are gateway endpoints, not routes, so they are not here.
        ["admin-cache-route"] = "Admin",
        ["catalog-categories-write-route"] = "Admin",
        ["catalog-categories-read-route"] = null,
        // Admin panel S14. /api/v1/basket/admin/** is admin-only; it wins over basket-route only because its Order (29)
        // is lower than basket-route's (30).
        ["basket-admin-route"] = "Admin",
        ["basket-route"] = "Authenticated",
        ["orders-route"] = "Authenticated",
        ["payments-route"] = "Authenticated",
        ["ordering-user-orders-route"] = "Authenticated",
        ["payment-user-payments-route"] = "Authenticated",
        // G3 (Admin panel S12). The notification delivery journal — the whole surface is operational, so there is no
        // anonymous read to carve out as there is under /api/v1/products.
        ["notifications-route"] = "Admin"
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
    public async Task TheProductExport_IsAdminOnly_ThoughProductReadsAreAnonymous()
    {
        // Admin panel S16. Without catalog-products-export-route this GET matches catalog-products-read-route and is
        // proxied for anyone, drafts included, with only Catalog's own policy left to refuse it.
        Assert.That(await Send(HttpMethod.Get, "/api/v1/products/export", token: null),
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await Send(HttpMethod.Get, "/api/v1/products/export", RouteAuthorizationApiFactory.UserToken()),
            Is.EqualTo(HttpStatusCode.Forbidden));

        AssertReachedProxy(
            await Send(HttpMethod.Get, "/api/v1/products/export", RouteAuthorizationApiFactory.AdminToken()),
            "an admin exporting products");

        // A product id still reads anonymously: the export route matches its own literal path only.
        AssertReachedProxy(
            await Send(HttpMethod.Get, "/api/v1/products/00000000-0000-0000-0000-000000000001", token: null),
            "an anonymous product read beside the export");
    }

    [Test]
    public async Task TheBulkActionsAndTheImport_AreAdminOnly()
    {
        // Admin panel S16. POSTs, so catalog-products-write-route already covers them; pinned so a later "bulk" route
        // with its own id cannot quietly arrive without a policy.
        foreach (var path in new[]
                 {
                     "/api/v1/products/bulk/publish",
                     "/api/v1/products/bulk/unpublish",
                     "/api/v1/products/bulk/delete",
                     "/api/v1/products/bulk/category",
                     "/api/v1/products/bulk/price",
                     "/api/v1/products/import"
                 })
        {
            Assert.That(await Send(HttpMethod.Post, path, token: null), Is.EqualTo(HttpStatusCode.Unauthorized), path);
            Assert.That(await Send(HttpMethod.Post, path, RouteAuthorizationApiFactory.UserToken()),
                Is.EqualTo(HttpStatusCode.Forbidden), path);
            AssertReachedProxy(await Send(HttpMethod.Post, path, RouteAuthorizationApiFactory.AdminToken()), $"an admin calling {path}");
        }
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
    public async Task TheNotificationJournal_IsAdminOnly()
    {
        // Admin panel S12 added notification-cluster and this route together. Notification itself requires the
        // notifications.read permission, so the gateway's role check and the service's permission check are two
        // different questions; this asserts the gateway half.
        Assert.That(await Send(HttpMethod.Get, "/api/v1/notifications", token: null),
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await Send(HttpMethod.Get, "/api/v1/notifications", RouteAuthorizationApiFactory.UserToken()),
            Is.EqualTo(HttpStatusCode.Forbidden));

        AssertReachedProxy(
            await Send(HttpMethod.Get, "/api/v1/notifications/stats", RouteAuthorizationApiFactory.AdminToken()),
            "an admin calling /api/v1/notifications/stats");
    }

    [Test]
    public async Task BasketAdminPaths_AreAdminOnly_WhileACustomersOwnBasketIsNot()
    {
        // Admin panel S14. Before basket-admin-route these paths matched basket-route and any signed-in customer was
        // proxied through; only Basket's own policies refused them. The S7 outbox paths are covered by the same route.
        foreach (var path in new[]
                 {
                     "/api/v1/basket/admin/carts",
                     "/api/v1/basket/admin/abandoned",
                     "/api/v1/basket/admin/outbox/dead-letters",
                     "/api/v1/basket/admin/outbox/dead-letters/details"
                 })
        {
            Assert.That(await Send(HttpMethod.Get, path, token: null), Is.EqualTo(HttpStatusCode.Unauthorized), path);
            Assert.That(await Send(HttpMethod.Get, path, RouteAuthorizationApiFactory.UserToken()),
                Is.EqualTo(HttpStatusCode.Forbidden), path);
            AssertReachedProxy(await Send(HttpMethod.Get, path, RouteAuthorizationApiFactory.AdminToken()), $"an admin calling {path}");
        }

        Assert.That(await Send(HttpMethod.Post, "/api/v1/basket/admin/outbox/dead-letters/replay",
                RouteAuthorizationApiFactory.UserToken()),
            Is.EqualTo(HttpStatusCode.Forbidden), "the replay is a write, and just as admin-only");

        AssertReachedProxy(
            await Send(HttpMethod.Get, "/api/v1/basket/user-1", RouteAuthorizationApiFactory.UserToken()),
            "a customer reading a basket outside the admin prefix");
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
        // This used to probe /api/v1/admin/users, pinning "no /api/v1/admin/** route exists yet" so
        // that the stage adding them had to do so deliberately. It worked: S4 added
        // admin-catalog-route and S6 admin-users-route, and this test went red at each; it went red
        // again at S19, when /api/v1/admin/settings — its probe since S6 — was built.
        //
        // Every admin path the plan reserves now exists, so it probes one that no stage defines. The
        // property is unchanged: there is deliberately NO catch-all /api/v1/admin/{**} route, so an
        // admin path nobody built answers "no such route" rather than being proxied somewhere.
        Assert.That(await Send(HttpMethod.Get, "/api/v1/admin/not-a-route", RouteAuthorizationApiFactory.AdminToken()),
            Is.EqualTo(HttpStatusCode.NotFound));

        // And the ones that DO exist must not be anonymous — the half that would actually be a hole. The last four are
        // the System page's (S19): three gateway endpoints and the proxied cache lever.
        foreach (var (method, path) in new[]
                 {
                     (HttpMethod.Get, "/api/v1/admin/users"),
                     (HttpMethod.Get, "/api/v1/admin/catalog/low-stock"),
                     (HttpMethod.Get, "/api/v1/admin/health"),
                     (HttpMethod.Get, "/api/v1/admin/settings"),
                     (HttpMethod.Get, "/api/v1/admin/feature-flags"),
                     (HttpMethod.Post, "/api/v1/admin/cache/invalidate")
                 })
        {
            Assert.That(await Send(method, path, token: null), Is.EqualTo(HttpStatusCode.Unauthorized), path);
        }
    }

    [Test]
    public async Task TheCacheLever_IsAdminOnly_AndIsProxiedToCatalog()
    {
        // Admin panel S19 (#88). Without admin-cache-route the path is a 404; with it but no policy, anyone is proxied
        // through and only Catalog's own system.manage check is left.
        const string path = "/api/v1/admin/cache/invalidate";

        Assert.That(await Send(HttpMethod.Post, path, token: null), Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await Send(HttpMethod.Post, path, RouteAuthorizationApiFactory.UserToken()), Is.EqualTo(HttpStatusCode.Forbidden));
        AssertReachedProxy(await Send(HttpMethod.Post, path, RouteAuthorizationApiFactory.AdminToken()), "an admin pulling the cache lever");
    }
}
