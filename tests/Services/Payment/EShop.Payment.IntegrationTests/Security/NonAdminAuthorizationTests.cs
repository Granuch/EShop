using System.Net;
using System.Net.Http.Json;
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
    };

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
