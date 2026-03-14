using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.IntegrationTests.Fixtures;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.Security;

/// <summary>
/// Security tests for Ordering API endpoints
/// </summary>
[TestFixture]
[Category("Integration")]
public class SecurityTests : IntegrationTestBase
{
    [Test]
    public async Task AllOrderEndpoints_WithoutAuth_ShouldReturnUnauthorized()
    {
        var endpoints = new[]
        {
            ("GET", $"/api/v1/orders/{Guid.NewGuid()}"),
            ("GET", "/api/v1/users/some-user/orders"),
            ("POST", $"/api/v1/orders/{Guid.NewGuid()}/cancel"),
            ("POST", $"/api/v1/orders/{Guid.NewGuid()}/ship"),
        };

        foreach (var (method, url) in endpoints)
        {
            HttpResponseMessage response;
            if (method == "POST")
            {
                response = await Client.PostAsJsonAsync(url, new { });
            }
            else
            {
                response = await Client.GetAsync(url);
            }

            response.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                because: $"{method} {url} should require authentication");
        }
    }

    [Test]
    public async Task CreateOrder_WithoutAuth_ShouldReturnUnauthorized()
    {
        var request = new CreateOrderRequest
        {
            UserId = "attacker",
            Street = "123 St",
            City = "City",
            Country = "US",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        var response = await Client.PostAsJsonAsync("/api/v1/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task HealthEndpoints_WithoutAuth_ShouldBeAccessible()
    {
        var healthEndpoints = new[] { "/health", "/health/ready", "/health/live" };

        foreach (var endpoint in healthEndpoints)
        {
            var response = await Client.GetAsync(endpoint);

            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                because: $"{endpoint} should be accessible without authentication");
        }
    }

    [Test]
    public async Task RootEndpoint_WithoutAuth_ShouldBeAccessible()
    {
        var response = await Client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
