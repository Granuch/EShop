using System.Net;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// POST /api/v1/orders/{id}/deliver — Ordering audit L2, decision D13. <c>Order.Deliver()</c> existed but
/// nothing called it, so no order could ever become Delivered.
/// </summary>
[TestFixture]
[Category("Integration")]
public class DeliverOrderTests : AuthenticatedIntegrationTestBase
{
    private async Task<Order> StoredAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .Orders.AsNoTracking().SingleAsync(o => o.Id == id);
    }

    [Test]
    public async Task DeliveringAShippedOrder_MarksItDelivered()
    {
        Guid id;
        using (var scope = CreateScope())
        {
            id = (await OrderingDataHelper.CreateShippedOrderAsync(scope.ServiceProvider)).Id;
        }

        var response = await Client.PostAsync($"/api/v1/orders/{id}/deliver", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var stored = await StoredAsync(id);
        stored.Status.Should().Be(OrderStatus.Delivered);
        stored.DeliveredAt.Should().NotBeNull();
    }

    [Test]
    public async Task DeliveringAnOrderNotYetShipped_IsAConflict_AndChangesNothing()
    {
        Guid id;
        using (var scope = CreateScope())
        {
            id = (await OrderingDataHelper.CreatePaidOrderAsync(scope.ServiceProvider)).Id;
        }

        var response = await Client.PostAsync($"/api/v1/orders/{id}/deliver", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StoredAsync(id)).Status.Should().Be(OrderStatus.Paid);
    }

    [Test]
    public async Task DeliveringTwice_IsAConflictTheSecondTime()
    {
        Guid id;
        using (var scope = CreateScope())
        {
            id = (await OrderingDataHelper.CreateShippedOrderAsync(scope.ServiceProvider)).Id;
        }

        (await Client.PostAsync($"/api/v1/orders/{id}/deliver", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.PostAsync($"/api/v1/orders/{id}/deliver", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task DeliveringAMissingOrder_IsNotFound()
    {
        (await Client.PostAsync($"/api/v1/orders/{Guid.NewGuid()}/deliver", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
