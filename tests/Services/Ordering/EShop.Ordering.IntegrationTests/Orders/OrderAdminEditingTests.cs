using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Admin panel S8, endpoints #57 and #59: <c>PUT /orders/{id}/items/{itemId}</c> and
/// <c>PUT /orders/{id}/shipping-address</c>. Nothing could change either before this stage — an
/// order's quantities could only be reached by removing a line and adding it back (impossible on a
/// single-line order, and it minted a new item id), and its address not at all.
///
/// <para>
/// Every failure case asserts the <b>stored</b> row, not only the status code: a 409 is returned
/// whether or not the write was rolled back, so a status-only assertion passes on an implementation
/// that persists the change and then reports failure.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderAdminEditingTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    private static UpdateShippingAddressRequest NewAddress(string city = "Shelbyville") => new()
    {
        Street = "9 New Ave",
        City = city,
        State = "IL",
        ZipCode = "62565",
        Country = "US"
    };

    private async Task<Order> SeedAsync(Func<IServiceProvider, Task<Order>> create)
    {
        using var scope = CreateScope();
        return await create(scope.ServiceProvider);
    }

    private async Task<Order> ReadStoredAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == orderId);
    }

    #region PUT /orders/{id}/items/{itemId}

    /// <summary>
    /// Also the <c>ValueGeneratedNever</c> shape from the other direction: the line is modified under
    /// an already-persisted parent, so EF must issue an UPDATE that matches the existing row.
    /// </summary>
    [Test]
    public async Task UpdateQuantity_OnAPendingOrder_ChangesTheLineAndTheTotal()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));
        var item = order.Items.Single();

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{item.Id}", new UpdateOrderItemQuantityRequest { Quantity = 4 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var stored = await ReadStoredAsync(order.Id);
        stored.Items.Single().Quantity.Should().Be(4);
        stored.Items.Single().Id.Should().Be(item.Id, "the line keeps its identity — that is why this is not remove-then-add");
        stored.TotalPrice.Should().Be(79.96m); // 19.99 * 4
    }

    [Test]
    public async Task UpdateQuantity_OnAMissingOrder_Is404()
    {
        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{Guid.NewGuid()}/items/{Guid.NewGuid()}",
            new UpdateOrderItemQuantityRequest { Quantity = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateQuantity_OnAMissingItem_Is404_NamingTheItem()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{Guid.NewGuid()}",
            new UpdateOrderItemQuantityRequest { Quantity = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("OrderItem.NotFound");
    }

    [Test]
    public async Task UpdateQuantity_OnAPaidOrder_Is409_AndChangesNothing()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreatePaidOrderAsync(sp, TestUserId));
        var item = order.Items.Single();

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{item.Id}", new UpdateOrderItemQuantityRequest { Quantity = 9 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Order.NotModifiable");

        var stored = await ReadStoredAsync(order.Id);
        stored.Items.Single().Quantity.Should().Be(2);
        stored.TotalPrice.Should().Be(59.98m);
    }

    [TestCase(0)]
    [TestCase(-3)]
    public async Task UpdateQuantity_WithANonPositiveQuantity_Is400_AndChangesNothing(int quantity)
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));
        var item = order.Items.Single();

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{item.Id}", new UpdateOrderItemQuantityRequest { Quantity = quantity });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadStoredAsync(order.Id)).Items.Single().Quantity.Should().Be(1);
    }

    /// <summary>Remove-then-add cannot express this: <c>RemoveItem</c> refuses to empty the order.</summary>
    [Test]
    public async Task UpdateQuantity_OnTheOnlyLine_IsAllowed()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{order.Items.Single().Id}",
            new UpdateOrderItemQuantityRequest { Quantity = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadStoredAsync(order.Id)).Items.Should().HaveCount(1);
    }

    /// <summary>
    /// The order detail is cached for five minutes under <c>order:{id}</c>. Without the eviction the
    /// edit would be invisible to every reader for the full TTL, and nothing would fail.
    /// </summary>
    [Test]
    public async Task UpdateQuantity_EvictsTheCachedOrderDetail()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var before = await Client.GetFromJsonAsync<OrderResponse>($"{OrdersEndpoint}/{order.Id}");
        before!.Items.Single().Quantity.Should().Be(1);

        (await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{order.Items.Single().Id}",
            new UpdateOrderItemQuantityRequest { Quantity = 6 })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await Client.GetFromJsonAsync<OrderResponse>($"{OrdersEndpoint}/{order.Id}");
        after!.Items.Single().Quantity.Should().Be(6);
    }

    #endregion

    #region PUT /orders/{id}/shipping-address

    [Test]
    public async Task UpdateAddress_OnAPendingOrder_ReplacesEveryField()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/shipping-address", NewAddress());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var stored = await ReadStoredAsync(order.Id);
        stored.ShippingAddress.Street.Should().Be("9 New Ave");
        stored.ShippingAddress.City.Should().Be("Shelbyville");
        stored.ShippingAddress.State.Should().Be("IL");
        stored.ShippingAddress.ZipCode.Should().Be("62565");
        stored.ShippingAddress.Country.Should().Be("US");
    }

    /// <summary>Paid is deliberately still editable: nothing has left the warehouse.</summary>
    [Test]
    public async Task UpdateAddress_OnAPaidOrder_IsStillAllowed()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreatePaidOrderAsync(sp, TestUserId));

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/shipping-address", NewAddress("Capital City"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadStoredAsync(order.Id)).ShippingAddress.City.Should().Be("Capital City");
    }

    [Test]
    public async Task UpdateAddress_OnAShippedOrder_Is409_AndChangesNothing()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateShippedOrderAsync(sp, TestUserId));

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/shipping-address", NewAddress());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Order.AddressNotModifiable");

        (await ReadStoredAsync(order.Id)).ShippingAddress.City.Should().Be("TestCity");
    }

    [Test]
    public async Task UpdateAddress_OnAMissingOrder_Is404()
    {
        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{Guid.NewGuid()}/shipping-address", NewAddress());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The address rules are <c>Address.Validate</c>'s, reported field by field — so the 400 names
    /// which field is wrong rather than failing inside the handler with one opaque message.
    /// </summary>
    [Test]
    public async Task UpdateAddress_WithAnInvalidCountry_Is400_NamingTheField_AndChangesNothing()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/shipping-address", NewAddress() with { Country = "USA" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Country");
        (await ReadStoredAsync(order.Id)).ShippingAddress.City.Should().Be("TestCity");
    }

    [Test]
    public async Task UpdateAddress_EvictsTheCachedOrderDetail()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var before = await Client.GetFromJsonAsync<OrderResponse>($"{OrdersEndpoint}/{order.Id}");
        before!.ShippingAddress.City.Should().Be("TestCity");

        (await Client.PutAsJsonAsync($"{OrdersEndpoint}/{order.Id}/shipping-address", NewAddress("Ogdenville")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await Client.GetFromJsonAsync<OrderResponse>($"{OrdersEndpoint}/{order.Id}");
        after!.ShippingAddress.City.Should().Be("Ogdenville");
    }

    #endregion
}
