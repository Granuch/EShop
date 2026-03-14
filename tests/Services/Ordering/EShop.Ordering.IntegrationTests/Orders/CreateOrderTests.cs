using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Integration tests for POST /api/v1/orders endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class CreateOrderTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    [Test]
    public async Task CreateOrder_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            UserId = TestUserId,
            Street = "456 Test Ave",
            City = "TestTown",
            State = "CA",
            ZipCode = "90210",
            Country = "US",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget X", Price = 15.99m, Quantity = 3 }
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/orders/");
    }

    [Test]
    public async Task CreateOrder_WithMultipleItems_ShouldReturnCreated()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            UserId = TestUserId,
            Street = "789 Multi St",
            City = "ItemCity",
            State = "TX",
            ZipCode = "75001",
            Country = "US",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Item A", Price = 10.00m, Quantity = 2 },
                new() { ProductId = Guid.NewGuid(), ProductName = "Item B", Price = 25.00m, Quantity = 1 }
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task CreateOrder_WithEmptyUserId_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            UserId = "",
            Street = "123 St",
            City = "City",
            Country = "US",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateOrder_WithNoItems_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            UserId = TestUserId,
            Street = "123 St",
            City = "City",
            Country = "US",
            Items = new List<CreateOrderItemRequest>()
        };

        // Act
        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateOrder_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        var request = new CreateOrderRequest
        {
            UserId = "user-1",
            Street = "123 St",
            City = "City",
            Country = "US",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = Guid.NewGuid(), ProductName = "Widget", Price = 10.00m, Quantity = 1 }
            }
        };

        // Act
        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
