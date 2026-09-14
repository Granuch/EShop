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

    /// <summary>Was pinned as 400; a missing order is a 404, as it already was from the ship endpoint.</summary>
    [Test]
    public async Task CancelOrder_WithNonExistentOrder_ShouldReturnNotFound()
    {
        // Arrange
        var request = new CancelOrderRequest { Reason = "Cancel please" };

        // Act
        var response = await Client.PostAsJsonAsync($"/api/v1/orders/{Guid.NewGuid()}/cancel", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Nothing refunds a cancelled paid order, so it must not be cancellable. The stored state is
    /// asserted as well: a 409 alone would pass if the cancel had been committed anyway.
    /// </summary>
    [Test]
    public async Task CancelOrder_WithPaidOrder_ShouldReturnConflict_AndStayPaid()
    {
        using var scope = CreateScope();
        var order = await OrderingDataHelper.CreatePaidOrderAsync(scope.ServiceProvider);

        var response = await Client.PostAsJsonAsync(
            $"/api/v1/orders/{order.Id}/cancel", new CancelOrderRequest { Reason = "Too late" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Order.NotCancellable");

        var stored = await Client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{order.Id}");
        stored!.Status.Should().Be(EShop.Ordering.Domain.Entities.OrderStatus.Paid);
        stored.CancelledAt.Should().BeNull();
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
