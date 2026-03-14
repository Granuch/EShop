using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Integration tests for POST /api/v1/orders/{id}/ship endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class ShipOrderTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task ShipOrder_WithPaidOrder_ShouldReturnNoContent()
    {
        // Arrange
        using var scope = CreateScope();
        var order = await OrderingDataHelper.CreatePaidOrderAsync(scope.ServiceProvider);

        // Act
        var response = await Client.PostAsync($"/api/v1/orders/{order.Id}/ship", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify order was actually shipped
        var getResponse = await Client.GetAsync($"/api/v1/orders/{order.Id}");
        var updatedOrder = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();
        updatedOrder!.Status.Should().Be(EShop.Ordering.Domain.Entities.OrderStatus.Shipped);
        updatedOrder.ShippedAt.Should().NotBeNull();
    }

    [Test]
    public async Task ShipOrder_WithPendingOrder_ShouldReturnBadRequest()
    {
        // Arrange — order is pending (not paid)
        using var scope = CreateScope();
        var order = await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider);

        // Act
        var response = await Client.PostAsync($"/api/v1/orders/{order.Id}/ship", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ShipOrder_WithNonExistentOrder_ShouldReturnBadRequest()
    {
        // Act
        var response = await Client.PostAsync($"/api/v1/orders/{Guid.NewGuid()}/ship", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ShipOrder_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.PostAsync($"/api/v1/orders/{Guid.NewGuid()}/ship", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
