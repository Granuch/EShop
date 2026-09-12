using System.Net;
using System.Net.Http.Json;
using System.Text;
using EShop.Ordering.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Api;

/// <summary>Ordering audit L8: what the API says about itself, and what it does with a bad request body.</summary>
[TestFixture]
[Category("Integration")]
public class ApiContractTests : AuthenticatedIntegrationTestBase
{
    /// <summary>
    /// Every endpoint declared <c>Produces&lt;object&gt;</c>, so the OpenAPI document described no response
    /// shapes at all. Structural, so a new endpoint cannot ship with an untyped success response either.
    /// </summary>
    [Test]
    public void EveryApiEndpoint_DeclaresTheShapeOfItsSuccessResponse()
    {
        var untyped = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(e => e.Metadata.OfType<IProducesResponseTypeMetadata>()
                .Where(m => m.StatusCode is StatusCodes.Status200OK or StatusCodes.Status201Created)
                .Where(m => m.Type is null || m.Type == typeof(object))
                .Select(m => $"{e.DisplayName} ({m.StatusCode})"))
            .ToList();

        untyped.Should().BeEmpty();
    }

    /// <summary>
    /// Audit L10 / D15: the API docs are served in every environment except Production — including Testing,
    /// which used to be the one environment where they were not.
    /// </summary>
    [TestCase("/openapi/v1.json")]
    [TestCase("/scalar/v1")]
    public async Task TheApiDocs_AreServedOutsideProduction(string path)
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Minimal-API binding swallowed the JsonException and answered with a bare 400 and an empty body, so
    /// a client could not tell what was wrong. It is now problem+json with an error code.
    /// </summary>
    [Test]
    public async Task AMalformedJsonBody_IsAProblemWithAnErrorCode()
    {
        using var body = new StringContent("{ \"userId\": ", Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/v1/orders", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("errorCode");
    }

    /// <summary>
    /// The route names the order; the body no longer has to repeat it. A body that names a different order
    /// is still refused.
    /// </summary>
    [Test]
    public async Task AddingAnItem_TakesTheOrderFromTheRoute()
    {
        Guid orderId;
        using (var scope = CreateScope())
        {
            orderId = (await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: TestUserId)).Id;
        }

        var withoutOrderId = await Client.PostAsJsonAsync($"/api/v1/orders/{orderId}/items",
            new { ProductId = Factory.Catalog.Add("Route Widget", 3m), Quantity = 1 });
        var withAnotherOrderId = await Client.PostAsJsonAsync($"/api/v1/orders/{orderId}/items",
            new { OrderId = Guid.NewGuid(), ProductId = Factory.Catalog.Add("Other Widget", 3m), Quantity = 1 });

        withoutOrderId.StatusCode.Should().Be(HttpStatusCode.NoContent, await withoutOrderId.Content.ReadAsStringAsync());
        withAnotherOrderId.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
