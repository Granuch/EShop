using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// frontend-contracts F-47, on real Postgres through the real pipeline. A customer changing their own Pending order must
/// leave Payment a message saying what the order now costs, written in the same save as the change — otherwise Payment
/// charges the old total and Ordering refuses the success, leaving a paid order Pending for good.
///
/// <para>
/// The outbox row is read the instant the response returns, with no poll waited for: an event enqueued anywhere but
/// in the change's own <c>SaveChanges</c> (a handler after the fact, a second save) would not be there yet, or could be
/// lost when that second step failed.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderTotalChangedOutboxTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    /// <summary>The order's owner, not an admin: this is the customer-reachable path the finding was about.</summary>
    protected override string TestUserRole => "User";

    private async Task<Order> SeedOrderAsync()
    {
        using var scope = CreateScope();
        return await OrderingDataHelper.CreateOrderAsync(scope.ServiceProvider, TestUserId);
    }

    private async Task<List<OutboxMessage>> TotalChangesForAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var id = orderId.ToString();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Type.Contains("OrderTotalChangedDomainEvent") && m.Payload.Contains(id))
            .ToListAsync();
    }

    [Test]
    public async Task ChangingAQuantity_WritesTheNewTotalToTheOutbox_InTheSameSave()
    {
        var order = await SeedOrderAsync();
        var item = order.Items.Single();

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{item.Id}", new UpdateOrderItemQuantityRequest { Quantity = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rows = await TotalChangesForAsync(order.Id);
        rows.Should().ContainSingle();
        rows[0].Payload.Should().Contain("59.97", "19.99 x 3, the order's whole new total");
    }

    [Test]
    public async Task SettingTheSameQuantity_WritesNothing()
    {
        var order = await SeedOrderAsync();
        var item = order.Items.Single();

        var response = await Client.PutAsJsonAsync(
            $"{OrdersEndpoint}/{order.Id}/items/{item.Id}", new UpdateOrderItemQuantityRequest { Quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TotalChangesForAsync(order.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task ARefusedChange_WritesNothing()
    {
        var order = await SeedOrderAsync();
        var item = order.Items.Single();

        // The order's only line: the aggregate refuses to remove it (400), and nothing may reach Payment.
        var response = await Client.DeleteAsync($"{OrdersEndpoint}/{order.Id}/items/{item.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TotalChangesForAsync(order.Id)).Should().BeEmpty();
    }
}
