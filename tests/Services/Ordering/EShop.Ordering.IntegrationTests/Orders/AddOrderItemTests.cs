using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// BUG-02 regression tests for POST /api/v1/orders/{id}/items.
///
/// <para>
/// <b>What breaks without the fix.</b> <c>OrderItem</c>'s constructor assigns
/// <c>Id = Guid.NewGuid()</c>, so the key is domain-assigned. Left as EF's default
/// <c>ValueGeneratedOnAdd</c>, EF sees an already-set store-generated key on a child discovered
/// under an <i>unchanged</i> parent, concludes the row must already exist, and issues an UPDATE
/// that matches nothing — <c>DbUpdateConcurrencyException</c>, surfaced as a 409.
/// <c>OrderingDbContext</c> maps the key <c>ValueGeneratedNever()</c> to prevent that.
/// </para>
///
/// <para>
/// <b>Why this endpoint and not order creation.</b> The defect hides well: an <c>Added</c> root
/// paints its whole graph <c>Added</c>, so creating an order together with its items works fine
/// and every existing test passes. Only "add a child to an already-persisted aggregate" breaks,
/// and until now nothing exercised that path — which is exactly why a Critical-ranked bug sat
/// behind a green suite.
/// </para>
///
/// <para>
/// Verified to be a real guard by reverting <c>ValueGeneratedNever()</c> in
/// <c>OrderingDbContext</c> and confirming these tests fail; see the commit message. They fail on
/// the InMemory provider as well as on Postgres, so this suite does not need a container.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class AddOrderItemTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    /// <summary>Creates an order and returns its id, so the aggregate is already persisted.</summary>
    private async Task<Guid> CreateOrderAsync()
    {
        var request = new CreateOrderRequest
        {
            UserId = TestUserId,
            Street = "1 Aggregate Way",
            City = "Persisted",
            State = "CA",
            ZipCode = "90210",
            Country = "US",
            Items =
            [
                new() { ProductId = Guid.NewGuid(), ProductName = "Original Item", Price = 10.00m, Quantity = 1 }
            ]
        };

        var response = await Client.PostAsJsonAsync(OrdersEndpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var location = response.Headers.Location!.ToString();
        return Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }

    private object NewItem(Guid orderId, string name = "Added Item", decimal price = 5.00m, int quantity = 2) => new
    {
        OrderId = orderId,
        ProductId = Guid.NewGuid(),
        ProductName = name,
        UnitPrice = price,
        Quantity = quantity
    };

    /// <summary>
    /// The core regression: adding an item to an order that already exists must succeed rather
    /// than returning 409.
    /// </summary>
    [Test]
    public async Task AddItem_ToAnAlreadyPersistedOrder_Succeeds()
    {
        var orderId = await CreateOrderAsync();

        var response = await Client.PostAsJsonAsync($"{OrdersEndpoint}/{orderId}/items", NewItem(orderId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a domain-assigned child key must not be mistaken for an existing row");
        response.StatusCode.Should().NotBe(HttpStatusCode.Conflict, "409 here is BUG-02 returning");
    }

    /// <summary>
    /// The item has to actually land, not merely avoid an error — a fix that silently dropped the
    /// write would satisfy a status-code-only assertion.
    /// </summary>
    [Test]
    public async Task AddItem_ActuallyPersistsTheNewItem()
    {
        var orderId = await CreateOrderAsync();

        var added = await Client.PostAsJsonAsync(
            $"{OrdersEndpoint}/{orderId}/items", NewItem(orderId, "Second Widget", 7.50m, 3));
        added.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fetched = await Client.GetAsync($"{OrdersEndpoint}/{orderId}");
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await fetched.Content.ReadAsStringAsync();
        body.Should().Contain("Second Widget", "the added item must be readable back from the aggregate");
        body.Should().Contain("Original Item", "adding an item must not replace the existing ones");
    }

    /// <summary>
    /// Repeated additions are where the old <c>OrderRepository.UpdateAsync</c> workaround was
    /// doing its work — it queried persisted item ids and forced <c>EntityState.Added</c> by hand.
    /// That workaround is deleted, so this is the case that proves the mapping replaced it.
    /// </summary>
    [Test]
    public async Task AddItem_TwiceInSuccession_BothSucceed()
    {
        var orderId = await CreateOrderAsync();

        var first = await Client.PostAsJsonAsync($"{OrdersEndpoint}/{orderId}/items", NewItem(orderId, "Item Two"));
        var second = await Client.PostAsJsonAsync($"{OrdersEndpoint}/{orderId}/items", NewItem(orderId, "Item Three"));

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await (await Client.GetAsync($"{OrdersEndpoint}/{orderId}")).Content.ReadAsStringAsync();
        body.Should().Contain("Item Two").And.Contain("Item Three");
    }

    /// <summary>
    /// The route/body mismatch guard is checked before the command is dispatched, so it must be a
    /// 400 rather than reaching the handler.
    /// </summary>
    [Test]
    public async Task AddItem_WithMismatchedRouteAndBodyOrderId_ReturnsBadRequest()
    {
        var orderId = await CreateOrderAsync();

        var response = await Client.PostAsJsonAsync(
            $"{OrdersEndpoint}/{orderId}/items", NewItem(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
