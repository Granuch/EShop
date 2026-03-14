using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Integration tests for GET /api/v1/users/{userId}/orders endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetOrdersByUserTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task GetOrdersByUser_WithExistingOrders_ShouldReturnOk()
    {
        // Arrange
        using var scope = CreateScope();
        await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: "user-with-orders");
        await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, userId: "user-with-orders");

        // Act
        var response = await Client.GetAsync("/api/v1/users/user-with-orders/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<OrderResponse>>();
        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterThanOrEqualTo(2);
        result.Should().OnlyContain(o => o.UserId == "user-with-orders");
    }

    [Test]
    public async Task GetOrdersByUser_WithNoOrders_ShouldReturnEmptyList()
    {
        // Act
        var response = await Client.GetAsync($"/api/v1/users/non-existent-user-{Guid.NewGuid()}/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<OrderResponse>>();
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetOrdersByUser_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.GetAsync("/api/v1/users/some-user/orders");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
