using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.IntegrationTests.Helpers;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.Caching;

/// <summary>
/// Audit H4, end to end, through the real CachingBehavior and CacheInvalidationBehavior over the
/// Testing host's in-memory IDistributedCache. Every test reads first, so the entry is cached, then
/// writes, then reads again: a write that invalidates nothing shows up as a stale second read.
///
/// <para>
/// Before the fix every write evicted the exact key <c>orders:user:{id}</c> while the list was cached
/// under <c>orders:user:{id}:p=..:ps=..:cur=..</c>, so the list tests here would all have read stale.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OrderCacheInvalidationTests : AuthenticatedIntegrationTestBase
{
    /// <summary>
    /// A host per test. The cache under test lives in the host, and some tests here write an order straight
    /// to the database — which, correctly, evicts nothing — so on a shared host a neighbour's cached list
    /// hides the new order and the test fails for the wrong reason.
    /// </summary>
    protected override bool UseFixtureScopedHost => false;

    private string ListUrl => $"/api/v1/users/{TestUserId}/orders";

    private async Task<PagedOrderResponse> ListAsync() =>
        (await Client.GetFromJsonAsync<PagedOrderResponse>(ListUrl))!;

    private async Task<OrderResponse> GetAsync(Guid id) =>
        (await Client.GetFromJsonAsync<OrderResponse>($"/api/v1/orders/{id}"))!;

    private async Task<Guid> CreateOrderAsync(int items = 1)
    {
        var request = new CreateOrderRequest
        {
            UserId = TestUserId,
            Street = "1 Cache St",
            City = "Staleville",
            State = "CA",
            ZipCode = "90210",
            Country = "US",
            Items = Enumerable.Range(1, items)
                .Select(i => new CreateOrderItemRequest { ProductId = Factory.Catalog.Add($"Cached Widget {i}", 10m), Quantity = 1 })
                .ToList()
        };

        var response = await Client.PostAsJsonAsync("/api/v1/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var location = response.Headers.Location!.ToString();
        return Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }

    [Test]
    public async Task ANewOrder_AppearsInAnAlreadyCachedList()
    {
        var before = await ListAsync();

        var id = await CreateOrderAsync();

        var after = await ListAsync();
        after.Items.Should().Contain(o => o.Id == id, "the list was cached before the order existed");
        after.TotalCount.Should().Be(before.TotalCount + 1);
    }

    [Test]
    public async Task ACancellation_ShowsInAnAlreadyCachedList()
    {
        var id = await CreateOrderAsync();
        (await ListAsync()).Items.Single(o => o.Id == id).Status.Should().Be(OrderStatus.Pending);

        (await Client.PostAsJsonAsync($"/api/v1/orders/{id}/cancel", new CancelOrderRequest { Reason = "changed my mind" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync()).Items.Single(o => o.Id == id).Status.Should().Be(OrderStatus.Cancelled);
        (await GetAsync(id)).Status.Should().Be(OrderStatus.Cancelled);
    }

    [Test]
    public async Task AnAddedItem_ShowsInTheAlreadyCachedOrderAndList()
    {
        var id = await CreateOrderAsync();
        (await GetAsync(id)).Items.Should().HaveCount(1);
        (await ListAsync()).Items.Single(o => o.Id == id).Items.Should().HaveCount(1);

        (await Client.PostAsJsonAsync($"/api/v1/orders/{id}/items",
                new AddOrderItemRequest { OrderId = id, ProductId = Factory.Catalog.Add("Late Addition", 5m), Quantity = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetAsync(id)).Items.Should().HaveCount(2);
        (await ListAsync()).Items.Single(o => o.Id == id).Items.Should().HaveCount(2);
    }

    [Test]
    public async Task ARemovedItem_ShowsInAnAlreadyCachedList()
    {
        var id = await CreateOrderAsync(items: 2);
        var order = await GetAsync(id);
        (await ListAsync()).Items.Single(o => o.Id == id).Items.Should().HaveCount(2);

        (await Client.DeleteAsync($"/api/v1/orders/{id}/items/{order.Items[0].Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync()).Items.Single(o => o.Id == id).Items.Should().HaveCount(1);
    }

    [Test]
    public async Task AShipment_ShowsInAnAlreadyCachedList()
    {
        Guid id;
        using (var scope = CreateScope())
        {
            id = (await OrderingDataHelper.CreatePaidOrderAsync(scope.ServiceProvider, TestUserId)).Id;
        }

        (await ListAsync()).Items.Single(o => o.Id == id).Status.Should().Be(OrderStatus.Paid);

        (await Client.PostAsync($"/api/v1/orders/{id}/ship", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync()).Items.Single(o => o.Id == id).Status.Should().Be(OrderStatus.Shipped);
    }
}
