using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Payment.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Security;

/// <summary>
/// Payment audit Stage 3 (H4, D2), after Ordering's and Catalog's tests of the same name. A policy downgrade is
/// invisible to a suite that only signs in as an admin: <c>RequireAuthorization("Admin")</c> →
/// <c>RequireAuthorization()</c> passes every admin test. So this signs in as a customer, and
/// <see cref="ExpectedPolicies"/> is checked against the endpoints the app actually registered. A new endpoint then
/// cannot ship without a deliberate policy decision recorded here.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class NonAdminAuthorizationTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Customer";
    protected override string TestUserId => "customer-1";

    /// <summary>Every route under <c>/api/</c> and its policy; <c>""</c> means "any signed-in caller".</summary>
    private static readonly Dictionary<string, string> ExpectedPolicies = new()
    {
        ["POST /api/v1/payments/create-intent"] = "",
        ["POST /api/v1/payments"] = "Admin",
        ["GET /api/v1/payments/{id:guid}"] = "",
        ["POST /api/v1/payments/{id:guid}/refund"] = "Admin",
        ["GET /api/v1/payments/simulation"] = "Admin",
        ["GET /api/v1/users/{userId}/payments"] = "SameUserOrAdmin",

        // Admin panel S10. Permission policies, not the Admin role (decision Q4c, §12.1: new admin endpoints declare
        // a permission). Behaviour is identical for every existing caller, because the Admin role bundles every
        // permission — so the two styles below are a migration in progress, not a disagreement. Payment's three older
        // admin endpoints move when a stage touches them.
        ["POST /api/v1/payments/offline"] = EShopPermissions.PaymentsWrite,
        ["GET /api/v1/payments"] = EShopPermissions.PaymentsRead,
        ["GET /api/v1/payments/stats"] = EShopPermissions.PaymentsRead,
        ["GET /api/v1/payments/export"] = EShopPermissions.PaymentsRead,

        // Admin panel S11. The timeline is a read; a replay re-applies a payment outcome, so it is a write — and
        // deliberately not payments.refund, which is held back for the one action that moves money outward.
        ["GET /api/v1/payments/{id:guid}/events"] = EShopPermissions.PaymentsRead,
        ["POST /api/v1/payments/webhooks/failed/replay"] = EShopPermissions.PaymentsWrite,
    };

    /// <summary>
    /// The behavioural half. A permission policy that was quietly redefined as
    /// <c>RequireAuthenticatedUser()</c> leaves every attribute in place, so the structural check above cannot see
    /// it — only a request with a valid non-admin token can.
    /// </summary>
    [TestCase("GET", "/api/v1/payments")]
    [TestCase("GET", "/api/v1/payments/stats")]
    [TestCase("GET", "/api/v1/payments/export")]
    public async Task ACustomer_CannotUseTheAdminReads(string method, string path)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// The Q6a endpoint is the one a customer would most like to reach: it declares an order paid without any money
    /// moving. The payment must stay as it was.
    /// </summary>
    [Test]
    public async Task ACustomer_CannotDeclareTheirOwnOrderPaid()
    {
        var seeded = await Factory.SeedPaymentAsync(TestUserId);

        var response = await Client.PostAsJsonAsync(
            "/api/v1/payments/offline", new { seeded.OrderId, Reference = "TRF-1" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var stored = (await Factory.FindByOrderIdAsync(seeded.OrderId))!;
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(stored.PaymentIntentId, Is.Empty);
        });
    }

    /// <summary>
    /// Admin panel S11. The timeline names operators, carries Stripe event ids and quotes decline reasons — none of
    /// it a customer's business, even about their own payment, which is why this asks for one they own.
    /// </summary>
    [Test]
    public async Task ACustomer_CannotReadTheTimelineOfTheirOwnPayment()
    {
        var seeded = await Factory.SeedPaymentAsync(TestUserId);

        var response = await Client.GetAsync($"/api/v1/payments/{seeded.Id}/events");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// Replaying a captured webhook re-applies a payment outcome. A customer reaching it could settle their own order
    /// by resurrecting a delivery.
    /// </summary>
    [Test]
    public async Task ACustomer_CannotReplayFailedWebhooks()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/payments/webhooks/failed/replay", new { });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// The H4 defect: a customer could settle their own order through the simulator, bypassing Stripe. The payment
    /// must stay as it was.
    /// </summary>
    [Test]
    public async Task ACustomer_CannotSettleAPayment_EvenTheirOwn()
    {
        var seeded = await Factory.SeedPaymentAsync(TestUserId);

        var response = await Client.PostAsJsonAsync("/api/v1/payments", new { seeded.OrderId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Factory.FindByOrderIdAsync(seeded.OrderId))!.Status, Is.EqualTo(PaymentStatus.Pending));
    }

    [Test]
    public void EveryEndpoint_CarriesItsExpectedPolicy()
    {
        var routes = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(m => m != "HEAD")
                .Select(m => (Route: $"{m} {e.RoutePattern.RawText!.TrimEnd('/')}", Endpoint: e)))
            .ToList();

        Assert.That(routes.Select(r => r.Route), Is.EquivalentTo(ExpectedPolicies.Keys),
            "an endpoint added or removed must be recorded in ExpectedPolicies with a deliberate policy");

        Assert.Multiple(() =>
        {
            foreach (var (route, endpoint) in routes)
            {
                var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(a => a.Policy ?? "")
                    .ToList();

                Assert.That(policies, Is.Not.Empty, $"{route} must require authorization");
                Assert.That(policies, Does.Contain(ExpectedPolicies[route]), $"{route} must carry its expected policy");
                Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>(), Is.Null, $"{route} must not be anonymous");
            }
        });
    }
}
