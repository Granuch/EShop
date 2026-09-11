using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Integration tests for POST /api/v1/orders endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class CreateOrderTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    private Guid Product(string name = "Widget", decimal price = 10.00m) => Factory.Catalog.Add(name, price);

    private static CreateOrderItemRequest Item(Guid productId, int quantity = 1) =>
        new() { ProductId = productId, Quantity = quantity };

    private CreateOrderRequest RequestFor(params CreateOrderItemRequest[] items) => new()
    {
        UserId = TestUserId,
        Street = "456 Test Ave",
        City = "TestTown",
        State = "CA",
        ZipCode = "90210",
        Country = "US",
        Items = [.. items]
    };

    private async Task<int> StoredOrderCountAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .Orders.CountAsync(o => o.UserId == TestUserId);
    }

    [Test]
    public async Task CreateOrder_WithValidData_ShouldReturnCreated()
    {
        var request = RequestFor(Item(Product("Widget X", 15.99m), 3));

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/orders/");
    }

    [Test]
    public async Task CreateOrder_WithMultipleItems_ShouldReturnCreated()
    {
        var request = RequestFor(Item(Product("Item A", 10.00m), 2), Item(Product("Item B", 25.00m)));

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Audit C1, the core guard. The body still carries the old name/price fields — as a client built
    /// against the previous contract would, or an attacker would — and every one of them must be
    /// ignored in favour of Catalog's. Before the fix this order was stored at 0.01 a unit.
    /// </summary>
    [Test]
    public async Task CreateOrder_PricesEveryLineFromCatalog_IgnoringClientSuppliedNameAndPrice()
    {
        var productId = Product("Widget X", 15.99m);
        var body = new
        {
            UserId = TestUserId,
            Street = "456 Test Ave",
            City = "TestTown",
            State = "CA",
            ZipCode = "90210",
            Country = "US",
            Items = new[]
            {
                new { ProductId = productId, Quantity = 3, ProductName = "Tampered", Price = 0.01m, UnitPrice = 0.01m }
            }
        };

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var order = await Client.GetFromJsonAsync<OrderResponse>(response.Headers.Location!);
        order!.Items.Should().ContainSingle();
        order.Items[0].ProductName.Should().Be("Widget X");
        order.Items[0].UnitPrice.Should().Be(15.99m);
        order.TotalPrice.Should().Be(47.97m);
    }

    [Test]
    public async Task CreateOrder_WithAProductCatalogDoesNotKnow_ShouldReturnBadRequest_AndStoreNothing()
    {
        var request = RequestFor(Item(Product()), Item(Guid.NewGuid()));

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!
            .ErrorCode.Should().Be("Order.ProductUnavailable");
        (await StoredOrderCountAsync()).Should().Be(0);
    }

    /// <summary>A Catalog outage must not read as the client's fault (400) or as a crash (500).</summary>
    [Test]
    public async Task CreateOrder_WhenCatalogIsUnavailable_ShouldReturnServiceUnavailable_AndStoreNothing()
    {
        var request = RequestFor(Item(Product()));
        Factory.Catalog.IsUnavailable = true;

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!
            .ErrorCode.Should().Be("Catalog.Unavailable");
        (await StoredOrderCountAsync()).Should().Be(0);
    }

    [Test]
    public async Task CreateOrder_WithTheSameProductTwice_ShouldReturnBadRequest()
    {
        var productId = Product();
        var request = RequestFor(Item(productId), Item(productId, 2));

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateOrder_WithEmptyUserId_ShouldReturnBadRequest()
    {
        var request = RequestFor(Item(Product())) with { UserId = "" };

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateOrder_WithNoItems_ShouldReturnBadRequest()
    {
        var request = RequestFor();

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateOrder_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        using var anonymous = Factory.CreateClient();
        var request = RequestFor(Item(Product()));

        var response = await anonymous.PostAsJsonAsync(OrdersEndpoint, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
