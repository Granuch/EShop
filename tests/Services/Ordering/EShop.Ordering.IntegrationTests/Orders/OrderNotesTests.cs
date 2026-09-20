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
/// Admin panel S9, endpoints #60 and #61: <c>POST</c> and <c>GET /api/v1/orders/{id}/notes</c>. There
/// was nowhere at all to record what an operator did about an order before this stage.
///
/// <para>
/// Both are Admin-only rather than <c>OrderOwnerOrAdmin</c>, unlike their neighbours, and that is the
/// security decision of the stage: a note is written <b>about</b> a customer, so the customer must not
/// be able to read it. The 403s live in <c>Security/NonAdminAuthorizationTests</c>.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderNotesTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    private async Task<Order> SeedAsync(Func<IServiceProvider, Task<Order>> create)
    {
        using var scope = Factory.Services.CreateScope();
        return await create(scope.ServiceProvider);
    }

    private Task<OrderNoteResponse[]?> ReadNotesAsync(Guid orderId)
        => Client.GetFromJsonAsync<OrderNoteResponse[]>($"{OrdersEndpoint}/{orderId}/notes");

    private Task<HttpResponseMessage> PostNoteAsync(Guid orderId, string body)
        => Client.PostAsJsonAsync($"{OrdersEndpoint}/{orderId}/notes", new AddOrderNoteRequest { Body = body });

    /// <summary>
    /// The <c>ValueGeneratedNever</c> shape the plan names for this stage: the note is a child added to
    /// an order that is <b>already persisted</b>. With a store-generated key EF decides the row already
    /// exists and issues an UPDATE that matches nothing — a 409 (BUG-02), not a 201.
    /// </summary>
    [Test]
    public async Task AddingANote_ToAnAlreadyPersistedOrder_Is201_Not409()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var response = await PostNoteAsync(order.Id, "called the customer");

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        created!.Id.Should().NotBeEmpty();

        var notes = await ReadNotesAsync(order.Id);
        notes.Should().ContainSingle();
        notes![0].Id.Should().Be(created.Id, "the 201 names the note the list then shows");
        notes[0].Body.Should().Be("called the customer");
        notes[0].OrderId.Should().Be(order.Id);
    }

    /// <summary>
    /// The author is taken from the caller's own claims. There is no field on the request that could
    /// carry one, so one operator cannot sign another's name to a record whose value is that it says
    /// who wrote it.
    /// </summary>
    [Test]
    public async Task ANote_IsSignedByTheCallerNotByTheRequest()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        (await PostNoteAsync(order.Id, "body")).StatusCode.Should().Be(HttpStatusCode.Created);

        var note = (await ReadNotesAsync(order.Id))![0];
        note.AuthorId.Should().Be(TestUserId);
        note.AuthorName.Should().Be(TestUserEmail, "the token carries an email claim and no display name");
    }

    /// <summary>An order nobody has annotated answers 200 with an empty list, not 404.</summary>
    [Test]
    public async Task AnOrderWithNoNotes_Is200_AndEmpty()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        var response = await Client.GetAsync($"{OrdersEndpoint}/{order.Id}/notes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<OrderNoteResponse[]>()).Should().BeEmpty();
    }

    [Test]
    public async Task Notes_AreNewestFirst()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        await PostNoteAsync(order.Id, "first");
        await PostNoteAsync(order.Id, "second");
        await PostNoteAsync(order.Id, "third");

        var notes = await ReadNotesAsync(order.Id);

        notes!.Select(n => n.Body).Should().Equal("third", "second", "first");
    }

    [Test]
    public async Task Notes_AreScopedToTheirOwnOrder()
    {
        var mine = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));
        var theirs = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        await PostNoteAsync(theirs.Id, "about the other order");

        (await ReadNotesAsync(mine.Id)).Should().BeEmpty();
        (await ReadNotesAsync(theirs.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task AddingANote_ToAMissingOrder_Is404()
    {
        var response = await PostNoteAsync(Guid.NewGuid(), "body");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Order.NotFound");
    }

    /// <summary>
    /// Nullable rather than an empty list for a reason: a 200 with <c>[]</c> here would tell a client
    /// that every id it invents names a real order.
    /// </summary>
    [Test]
    public async Task ReadingNotes_OfAMissingOrder_Is404_NotAnEmptyList()
    {
        var response = await Client.GetAsync($"{OrdersEndpoint}/{Guid.NewGuid()}/notes");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Order.NotFound");
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task ANoteWithABlankBody_Is400_AndStoresNothing(string body)
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        (await PostNoteAsync(order.Id, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ReadNotesAsync(order.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task ANoteLongerThanTheColumn_Is400_NotA500()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        (await PostNoteAsync(order.Id, new string('x', OrderNote.MaxBodyLength + 1)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the validator refuses it before Postgres 22001 can");

        (await ReadNotesAsync(order.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task ANoteAtExactlyTheLimit_IsStoredWhole()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        (await PostNoteAsync(order.Id, new string('x', OrderNote.MaxBodyLength)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await ReadNotesAsync(order.Id))![0].Body.Should().HaveLength(OrderNote.MaxBodyLength);
    }

    /// <summary>
    /// The cap is a <c>COUNT</c> pre-check in the handler, because <c>Order._notes</c> is never loaded
    /// and an aggregate-side check would compare against an empty collection forever. Only a test with
    /// real rows can tell the two apart — the handler's unit test mocks the count.
    /// </summary>
    [Test]
    public async Task AnOrderAtTheNoteLimit_RefusesAnother()
    {
        var order = await SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<OrderingDbContext>();
            var seeded = await OrderingDataHelper.CreateOrderAsync(sp, TestUserId);
            for (var i = 0; i < Order.MaxNotes; i++)
            {
                seeded.AddNote("seed-admin", "Seed Admin", $"note {i}");
            }
            await db.SaveChangesAsync();
            return seeded;
        });

        var response = await PostNoteAsync(order.Id, "one too many");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Order.NoteLimitReached");

        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .OrderNotes.AsNoTracking().CountAsync(n => n.OrderId == order.Id))
            .Should().Be(Order.MaxNotes, "the refused write stored nothing");
    }

    /// <summary>Most of what is worth writing down happens after the order is final.</summary>
    [Test]
    public async Task ANote_CanBeAddedToACancelledOrder()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));
        (await Client.PostAsJsonAsync($"{OrdersEndpoint}/{order.Id}/cancel",
            new CancelOrderRequest { Reason = "changed mind" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await PostNoteAsync(order.Id, "refund processed by finance"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// A note changes nothing about the order itself — not its status, not its total, and not the
    /// concurrency token a concurrent edit is checked against.
    /// </summary>
    [Test]
    public async Task ANote_LeavesTheOrderItselfAlone()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));
        var before = await ReadStoredOrderAsync(order.Id);

        (await PostNoteAsync(order.Id, "body")).StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await ReadStoredOrderAsync(order.Id);
        after.Status.Should().Be(before.Status);
        after.TotalPrice.Should().Be(before.TotalPrice);
        after.Version.Should().Be(before.Version, "annotating an order must not collide with editing it");
    }

    /// <summary>
    /// Notes appear in no cached response, which is why <c>AddOrderNoteCommand</c> declares no cache
    /// keys — <c>OrderDto</c> does not carry them, so <c>order:{id}</c> stays correct.
    /// </summary>
    [Test]
    public async Task ANote_DoesNotAppearInTheOrderDetail()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));
        await Client.GetFromJsonAsync<OrderResponse>($"{OrdersEndpoint}/{order.Id}");

        (await PostNoteAsync(order.Id, "internal only")).StatusCode.Should().Be(HttpStatusCode.Created);

        var detail = await Client.GetAsync($"{OrdersEndpoint}/{order.Id}");
        (await detail.Content.ReadAsStringAsync()).Should().NotContain("internal only");
    }

    /// <summary>A note is not a state transition, so it writes no history row.</summary>
    [Test]
    public async Task ANote_WritesNoHistoryRow()
    {
        var order = await SeedAsync(sp => OrderingDataHelper.CreateOrderAsync(sp, TestUserId));

        (await PostNoteAsync(order.Id, "body")).StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .OrderStatusHistoryEntries.AsNoTracking().CountAsync(h => h.OrderId == order.Id))
            .Should().Be(1, "only the creation row");
    }

    private async Task<Order> ReadStoredOrderAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>()
            .Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }
}
