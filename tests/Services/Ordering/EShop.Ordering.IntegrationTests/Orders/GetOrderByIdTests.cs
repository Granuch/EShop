using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Integration tests for GET /api/v1/orders/{id} endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetOrderByIdTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task GetOrderById_WithExistingOrder_ShouldReturnOk()
    {
        // Arrange
        using var scope = CreateScope();
        var order = await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider);

        // Act
        var response = await Client.GetAsync($"/api/v1/orders/{order.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<OrderResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(order.Id);
        result.Items.Should().NotBeEmpty();
        result.ShippingAddress.Should().NotBeNull();
        result.ShippingAddress.Street.Should().Be("123 Test St");
    }

    [Test]
    public async Task GetOrderById_WithNonExistentOrder_ShouldReturnNotFound()
    {
        // Act
        var response = await Client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetOrderById_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
