using System.Net;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Security;

/// <summary>
/// Admin panel S14. Who may use Basket's admin family, <c>/api/v1/basket/admin/**</c>. A suite that only signs in as an
/// admin cannot tell a permission policy from <c>RequireAuthorization()</c>, so every endpoint is also asked by a
/// customer, and each permission by a token that holds it and no role at all.
/// </summary>
[TestFixture]
[Category("Integration")]
public class BasketAdminAuthorizationTests
{
    private const string Carts = "/api/v1/basket/admin/carts";
    private const string Abandoned = "/api/v1/basket/admin/abandoned";
    private const string DeadLetterDetails = "/api/v1/basket/admin/outbox/dead-letters/details";

    /// <summary>
    /// Every endpoint under the admin prefix, with the policies it carries. A route added there without a line here fails
    /// <see cref="EveryAdminEndpoint_IsListedHere_WithItsPolicies"/>, so one cannot ship on the owner-or-admin-read policy,
    /// or on none. The two S7 outbox endpoints still ask for the <c>Admin</c> role.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedPolicies = new(StringComparer.Ordinal)
    {
        ["GET /api/v1/basket/admin/carts"] = [EShopPermissions.BasketsRead],
        ["GET /api/v1/basket/admin/abandoned"] = [EShopPermissions.BasketsRead],
        ["GET /api/v1/basket/admin/outbox/dead-letters/details"] = [EShopPermissions.SystemManage],
        ["GET /api/v1/basket/admin/outbox/dead-letters"] = ["Admin"],
        ["POST /api/v1/basket/admin/outbox/dead-letters/replay"] = ["Admin"]
    };

    private BasketApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new BasketApiFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    [TestCase(Carts)]
    [TestCase(Abandoned)]
    [TestCase(DeadLetterDetails)]
    public async Task Anonymous_IsUnauthorized(string path)
    {
        using var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestCase(Carts)]
    [TestCase(Abandoned)]
    [TestCase(DeadLetterDetails)]
    public async Task ACustomer_IsForbidden(string path)
    {
        using var customer = _factory.CreateClientFor("user-1");

        (await customer.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestCase(Carts)]
    [TestCase(Abandoned)]
    [TestCase(DeadLetterDetails)]
    public async Task AnAdmin_IsAdmitted(string path)
    {
        using var admin = _factory.CreateClientFor("ops-1", isAdmin: true);

        (await admin.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The carts are opened by <c>baskets.read</c> itself, not by the role that happens to bundle it.</summary>
    [Test]
    public async Task TheBasketReadPermission_OpensTheCarts_AndNotTheOutbox()
    {
        using var reader = _factory.CreateClientWithPermissions("support-1", EShopPermissions.BasketsRead);

        (await reader.GetAsync(Carts)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.GetAsync(Abandoned)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.GetAsync(DeadLetterDetails)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TheSystemPermission_OpensTheDeadLetters_AndNotTheCarts()
    {
        using var operatorClient = _factory.CreateClientWithPermissions("ops-2", EShopPermissions.SystemManage);

        (await operatorClient.GetAsync(DeadLetterDetails)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorClient.GetAsync(Carts)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ADifferentPermission_OpensNothingHere()
    {
        using var other = _factory.CreateClientWithPermissions("ops-3", EShopPermissions.OrdersRead);

        (await other.GetAsync(Carts)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync(DeadLetterDetails)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public void EveryAdminEndpoint_IsListedHere_WithItsPolicies()
    {
        var actual = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/basket/admin", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => (
                    Route: $"{method} {e.RoutePattern.RawText!.TrimEnd('/')}",
                    Policies: e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                        .Select(a => a.Policy)
                        .OfType<string>()
                        .Order(StringComparer.Ordinal)
                        .ToArray())))
            .ToDictionary(x => x.Route, x => x.Policies, StringComparer.Ordinal);

        actual.Keys.Should().BeEquivalentTo(ExpectedPolicies.Keys,
            "an endpoint under /api/v1/basket/admin was added or removed — list it above with the policy it must carry");

        foreach (var (route, expected) in ExpectedPolicies)
        {
            actual[route].Should().Equal(expected, $"{route} carries the wrong policies");
        }
    }
}
