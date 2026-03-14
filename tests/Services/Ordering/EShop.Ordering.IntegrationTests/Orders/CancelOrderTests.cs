using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Integration tests for POST /api/v1/orders/{id}/cancel endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class CancelOrderTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task CancelOrder_WithPendingOrder_ShouldReturnNoContent()
    {
        // Arrange
        using var scope = CreateScope();
        var order = await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider);

        var request = new CancelOrderRequest { Reason = "Changed my mind" };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify order was actually cancelled
        var getResponse = await Client.GetAsync($"/api/v1/orders/{order.Id}");
        var updatedOrder = await getResponse.Content.ReadFromJsonAsync<OrderResponse>();
        updatedOrder!.Status.Should().Be(EShop.Ordering.Domain.Entities.OrderStatus.Cancelled);
        updatedOrder.CancellationReason.Should().Be("Changed my mind");
    }

    [Test]
    public async Task CancelOrder_WithNonExistentOrder_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CancelOrderRequest { Reason = "Cancel please" };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/v1/orders/{Guid.NewGuid()}/cancel", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CancelOrder_WithEmptyReason_ShouldReturnBadRequest()
    {
        // Arrange
        using var scope = CreateScope();
        var order = await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider);

        var request = new CancelOrderRequest { Reason = "" };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
