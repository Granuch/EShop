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
/// Admin panel S9, endpoint #62: <c>GET /api/v1/orders/{id}/history</c>. Nothing recorded an order's
/// transitions before this stage — the order carried four nullable timestamps and no record at all of
/// what a refund or a cancellation came from.
///
/// <para>
/// The load-bearing claim of this fixture is <b>when</b> the rows exist. They are appended by the
/// aggregate inside the transition's own <c>SaveChanges</c>, so every assertion here reads the
/// database immediately after the HTTP response returns. A history fed from the domain events instead
/// — the obvious alternative, and the one §3 of the plan describes — could not pass these: in this
/// repo a domain event is written to <c>outbox_messages</c> and its handler runs later, in the outbox
/// processor's own scope and transaction, so the rows would appear a polling interval afterwards and
/// only for the four transitions that raise an event at all.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderStatusHistoryTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    private async Task<Order> SeedAsync(Func<IServiceProvider, Task<Order>> create)
    {
        using var scope = Factory.Services.CreateScope();
        return await create(scope.ServiceProvider);
    }

    private async Task<List<OrderStatusHistory>> ReadStoredHistoryAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .OrderStatusHistoryEntries.AsNoTracking()
            .Where(h => h.OrderId == orderId)
            .OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
            .ToListAsync();
    }

    private Task<OrderStatusHistoryResponse[]?> ReadHistoryAsync(Guid orderId)
        => Client.GetFromJsonAsync<OrderStatusHistoryResponse[]>($"{OrdersEndpoint}/{orderId}/history");

    /// <summary>
    /// The opening row is written when the order is created, so an order that never moved has a
    /// timeline rather than an empty one that reads as "no data".
    /// </summary>
    [Test]
    public async Task ANewOrder_AlreadyHasItsOpeningRow()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var history = await ReadHistoryAsync(order.Id);

        history.Should().HaveCount(1);
        history![0].FromStatus.Should().BeNull();
        history[0].ToStatus.Should().Be(OrderStatus.Pending);
        history[0].OrderId.Should().Be(order.Id);
    }

    /// <summary>
    /// Also the <c>ValueGeneratedNever</c> shape: every row after the first is a child added to an
    /// order that is <b>already persisted</b>, which is exactly the case a store-generated key turns
    /// into an UPDATE matching nothing and a 409 (BUG-02).
    /// </summary>
    [Test]
    public async Task EachTransition_AppendsExactlyOneRow_AsItHappens()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreatePaidOrderAsync(sp, TestUserId));

        (await ReadHistoryAsync(order.Id)).Should().HaveCount(2, "created and paid");

        (await Client.PostAsync($"{OrdersEndpoint}/{order.Id}/ship", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Read straight away: no outbox poll has happened, and none is waited for.
        (await ReadHistoryAsync(order.Id)).Should().HaveCount(3);

        (await Client.PostAsync($"{OrdersEndpoint}/{order.Id}/deliver", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = await ReadHistoryAsync(order.Id);
        history.Should().HaveCount(4);
        history!.Select(h => (h.FromStatus, h.ToStatus)).Should().Equal(
            (null, OrderStatus.Pending),
            (OrderStatus.Pending, OrderStatus.Paid),
            (OrderStatus.Paid, OrderStatus.Shipped),
            (OrderStatus.Shipped, OrderStatus.Delivered));
    }

    /// <summary>
    /// <c>Order.Deliver</c> raises no domain event whatsoever, so this row cannot come from one. It is
    /// the single clearest reason the history is written by the aggregate.
    /// </summary>
    [Test]
    public async Task ADeliver_IsRecorded_ThoughItRaisesNoDomainEvent()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateShippedOrderAsync(sp, TestUserId));

        (await Client.PostAsync($"{OrdersEndpoint}/{order.Id}/deliver", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await ReadStoredHistoryAsync(order.Id);
        stored.Should().ContainSingle(h => h.ToStatus == OrderStatus.Delivered)
            .Which.FromStatus.Should().Be(OrderStatus.Shipped);
    }

    [Test]
    public async Task ACancellation_RecordsItsReason()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        (await Client.PostAsJsonAsync($"{OrdersEndpoint}/{order.Id}/cancel",
            new CancelOrderRequest { Reason = "customer changed their mind" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = await ReadHistoryAsync(order.Id);
        var cancelled = history!.Single(h => h.ToStatus == OrderStatus.Cancelled);
        cancelled.FromStatus.Should().Be(OrderStatus.Pending);
        cancelled.Reason.Should().Be("customer changed their mind");
    }

    /// <summary>Only a cancellation carries a reason today; the rest leave it null rather than inventing one.</summary>
    [Test]
    public async Task ATransitionWithNoReason_LeavesItNull()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreatePaidOrderAsync(sp, TestUserId));

        (await Client.PostAsync($"{OrdersEndpoint}/{order.Id}/ship", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadHistoryAsync(order.Id))!.Single(h => h.ToStatus == OrderStatus.Shipped)
            .Reason.Should().BeNull();
    }

    /// <summary>
    /// A rejected transition writes nothing, with no compensating delete anywhere — which is what
    /// "appended inside the transition" buys.
    /// </summary>
    [Test]
    public async Task ARefusedTransition_WritesNoRow()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var response = await Client.PostAsync($"{OrdersEndpoint}/{order.Id}/ship", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a pending order cannot ship");
        (await ReadStoredHistoryAsync(order.Id)).Should().HaveCount(1, "only the creation row");
    }

    /// <summary>
    /// The actor is <c>CreatedBy</c>, stamped by <c>BaseDbContext</c> from the ambient user context, so
    /// no domain method had to grow an actor parameter. A seeded order is created outside any request
    /// and so reads "system"; the ship that follows carries the signed-in operator's id.
    /// </summary>
    [Test]
    public async Task EachRow_NamesWhoCausedIt()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreatePaidOrderAsync(sp, TestUserId));

        (await Client.PostAsync($"{OrdersEndpoint}/{order.Id}/ship", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = await ReadHistoryAsync(order.Id);
        history!.Single(h => h.ToStatus == OrderStatus.Pending).ActorId
            .Should().Be("system", "nothing created it over HTTP");
        history.Single(h => h.ToStatus == OrderStatus.Shipped).ActorId
            .Should().Be(TestUserId, "the admin who drove the transition");
    }

    [Test]
    public async Task History_IsOldestFirst()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateShippedOrderAsync(sp, TestUserId));

        var history = await ReadHistoryAsync(order.Id);

        history!.Select(h => h.OccurredAt).Should().BeInAscendingOrder();
        history[0].FromStatus.Should().BeNull("a timeline read backwards has its From/To chain backwards");
    }

    [Test]
    public async Task History_OfAMissingOrder_Is404()
    {
        var response = await Client.GetAsync($"{OrdersEndpoint}/{Guid.NewGuid()}/history");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Order.NotFound");
    }

    /// <summary>One order's history is its own; a second order's transitions never leak into it.</summary>
    [Test]
    public async Task History_IsScopedToItsOwnOrder()
    {
        var mine = await SeedAsync(sp => OrderingDataHelper.CreatePaidOrderAsync(sp, TestUserId));
        var theirs = await SeedAsync(sp => OrderingDataHelper.CreatePaidOrderAsync(sp, TestUserId));

        (await Client.PostAsync($"{OrdersEndpoint}/{theirs.Id}/ship", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadHistoryAsync(mine.Id)).Should().HaveCount(2);
        (await ReadHistoryAsync(mine.Id))!.Should().OnlyContain(h => h.OrderId == mine.Id);
    }

    /// <summary>An edit is not a transition, so it leaves the timeline alone.</summary>
    [Test]
    public async Task AnEditThatIsNotATransition_WritesNoRow()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        (await Client.PutAsJsonAsync($"{OrdersEndpoint}/{order.Id}/shipping-address",
            new UpdateShippingAddressRequest
            {
                Street = "9 New Ave",
                City = "Shelbyville",
                State = "IL",
                ZipCode = "62565",
                Country = "US"
            })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadStoredHistoryAsync(order.Id)).Should().HaveCount(1);
    }
}
