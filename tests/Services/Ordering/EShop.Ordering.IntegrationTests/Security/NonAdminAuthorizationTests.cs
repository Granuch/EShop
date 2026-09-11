using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Security;

/// <summary>
/// Ordering audit H1/M12. A signed-in caller who is <b>not</b> an admin.
///
/// <para>
/// Every other authenticated fixture runs as <c>Admin</c>, and both <c>OrderOwnerOrAdminHandler</c>
/// and <c>SameUserOrAdminHandler</c> short-circuit on the Admin role — so until this fixture the
/// ownership checks never executed over HTTP. That is how the owner handler could be registered as a
/// Singleton holding a scoped repository without a single test noticing.
/// </para>
///
/// <para>
/// <see cref="ExpectedPolicies"/> is checked against the endpoints the app actually registered, so a
/// new endpoint cannot ship without a deliberate policy decision recorded here.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class NonAdminAuthorizationTests : AuthenticatedIntegrationTestBase
{
    private const string OtherUserId = "someone-else";

    protected override string TestUserRole => "Customer";
    protected override string TestUserId => "customer-1";
    protected override string TestUserEmail => "customer@test.com";

    /// <summary>Every route under <c>/api/</c> and its policy; <c>""</c> means "any signed-in caller".</summary>
    private static readonly Dictionary<string, string> ExpectedPolicies = new()
    {
        ["POST /api/v1/orders"] = "",
        ["GET /api/v1/orders"] = "Admin",
        ["GET /api/v1/orders/{id:guid}"] = "OrderOwnerOrAdmin",
        ["POST /api/v1/orders/{id:guid}/items"] = "OrderOwnerOrAdmin",
        ["DELETE /api/v1/orders/{id:guid}/items/{itemId:guid}"] = "OrderOwnerOrAdmin",
        ["POST /api/v1/orders/{id:guid}/cancel"] = "OrderOwnerOrAdmin",
        ["POST /api/v1/orders/{id:guid}/ship"] = "Admin",
        ["GET /api/v1/users/{userId}/orders"] = "SameUserOrAdmin",
    };

    private static readonly string[] OwnerOnlyRoutes =
    [
        "GET /api/v1/orders/{id:guid}",
        "POST /api/v1/orders/{id:guid}/items",
        "DELETE /api/v1/orders/{id:guid}/items/{itemId:guid}",
        "POST /api/v1/orders/{id:guid}/cancel",
    ];

    private async Task<Order> CreateOrderForAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        return await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId);
    }

    [Test]
    public async Task TheOwner_CanReadTheirOwnOrder()
    {
        var order = await CreateOrderForAsync(TestUserId);

        var response = await Client.GetAsync($"/api/v1/orders/{order.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Several owner checks at once. The handler used to hold one root-scoped DbContext for every
    /// request; this is not what proves the fix (a race is not deterministic — the factory's scope
    /// validation is), but it pins that concurrent owners are all served.
    /// </summary>
    [Test]
    public async Task ConcurrentOwnerChecks_AreAllServed()
    {
        var order = await CreateOrderForAsync(TestUserId);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => Client.GetAsync($"/api/v1/orders/{order.Id}")));

        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
    }

    [TestCaseSource(nameof(OwnerOnlyRoutes))]
    public async Task ANonOwner_IsForbidden(string route)
    {
        var order = await CreateOrderForAsync(OtherUserId);
        var method = route[..route.IndexOf(' ')];
        var path = route[(method.Length + 1)..]
            .Replace("{id:guid}", order.Id.ToString())
            .Replace("{itemId:guid}", order.Items.First().Id.ToString());

        object? body = route switch
        {
            "POST /api/v1/orders/{id:guid}/items" => new AddOrderItemRequest
            {
                OrderId = order.Id,
                ProductId = Factory.Catalog.Add("Forbidden Item", 1m),
                Quantity = 1
            },
            "POST /api/v1/orders/{id:guid}/cancel" => new CancelOrderRequest { Reason = "not mine" },
            _ => null
        };

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = body is null ? null : JsonContent.Create(body, body.GetType())
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{route} on another user's order must be refused; body: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>A 403 alone would pass if the write ran and only the response reported failure.</summary>
    [Test]
    public async Task AForbiddenCancel_ChangesNothing()
    {
        var order = await CreateOrderForAsync(OtherUserId);

        (await Client.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new CancelOrderRequest { Reason = "not mine" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        stored.Status.Should().Be(OrderStatus.Pending);
    }

    [Test]
    public async Task ANonAdmin_ReadsOnlyTheirOwnOrderList()
    {
        (await Client.GetAsync($"/api/v1/users/{TestUserId}/orders"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetAsync($"/api/v1/users/{OtherUserId}/orders"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ANonAdmin_CannotUseAdminRoutes()
    {
        var order = await CreateOrderForAsync(TestUserId);

        (await Client.GetAsync("/api/v1/orders"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.PostAsync($"/api/v1/orders/{order.Id}/ship", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ANonAdmin_CannotCreateAnOrderForSomeoneElse()
    {
        var request = new CreateOrderRequest
        {
            UserId = OtherUserId,
            Street = "1 Impersonation St",
            City = "Nowhere",
            State = "CA",
            ZipCode = "90210",
            Country = "US",
            Items = [new() { ProductId = Factory.Catalog.Add("Widget", 10m), Quantity = 1 }]
        };

        var response = await Client.PostAsJsonAsync("/api/v1/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

        routes.Select(r => r.Route).Should().BeEquivalentTo(ExpectedPolicies.Keys,
            "an endpoint added or removed must be recorded in ExpectedPolicies with a deliberate policy");

        foreach (var (route, endpoint) in routes)
        {
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy ?? "")
                .ToList();

            policies.Should().NotBeEmpty($"{route} must require authorization");
            policies.Should().Contain(ExpectedPolicies[route], $"{route} must carry its expected policy");
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull($"{route} must not be anonymous");
        }
    }
}
