using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Checkout;

/// <summary>
/// Basket audit S3 (C2, H1, H2, M7; D2, D3), end to end on real Redis: what a checkout leaves behind — one outbox entry
/// whose id is the checkoutId, no basket, no index entries, a 24-hour completed marker — and when it leaves nothing.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CheckoutTests
{
    private const string PendingOutbox = "basket:outbox:pending";

    private static readonly object CheckoutBody = new
    {
        shippingAddress = new
        {
            street = "1 Main St",
            city = "Springfield",
            state = "IL",
            zipCode = "62701",
            country = "US"
        },
        paymentMethod = "card"
    };

    private sealed record CheckoutResponse(Guid CheckoutId);

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static async Task AddAsync(HttpClient client, string userId, Guid productId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId, quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostCheckoutAsync(HttpClient client, string userId)
        => client.PostAsJsonAsync($"/api/v1/basket/{userId}/checkout", CheckoutBody);

    private static async Task<Guid> CheckOutAsync(HttpClient client, string userId)
    {
        var response = await PostCheckoutAsync(client, userId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CheckoutResponse>())!.CheckoutId;
    }

    /// <summary>The queued envelopes, newest first (the outbox pushes on the left).</summary>
    private static async Task<List<JsonElement>> PendingAsync(BasketApiFactory factory)
        => (await factory.Redis.GetDatabase().ListRangeAsync(PendingOutbox))
            .Select(value => JsonDocument.Parse(value.ToString()).RootElement.Clone())
            .ToList();

    private static JsonElement PayloadOf(JsonElement envelope)
        => JsonDocument.Parse(envelope.GetProperty("payload").GetString()!).RootElement.Clone();

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    [Test]
    public async Task ACheckout_QueuesOneEventUnderTheCheckoutId_RemovesTheBasket_AndRecordsTheCheckout()
    {
        await using var factory = new BasketApiFactory();
        var productId = factory.Catalog.Add("Mug", 12.50m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, productId);

        var checkoutId = await CheckOutAsync(client, userId);

        var pending = await PendingAsync(factory);
        pending.Should().ContainSingle("the event is written in the same transaction that removes the basket (H1)");
        pending[0].GetProperty("id").GetGuid().Should().Be(checkoutId,
            "D3: the envelope id is published as the MessageId Ordering deduplicates on");
        var payload = PayloadOf(pending[0]);
        payload.GetProperty("eventId").GetGuid().Should().Be(checkoutId);
        payload.GetProperty("userId").GetString().Should().Be(userId);
        payload.GetProperty("items")[0].GetProperty("productId").GetGuid().Should().Be(productId);

        var database = factory.Redis.GetDatabase();
        (await database.KeyExistsAsync($"basket:user:{userId}")).Should().BeFalse();
        (await database.SetContainsAsync($"basket:product:{productId}:users", userId)).Should().BeFalse(
            "a checked-out basket must drop out of price sync's index");

        var marker = $"basket:checkout:completed:{userId}";
        ((string?)await database.StringGetAsync(marker)).Should().Be(checkoutId.ToString("D"));
        (await database.KeyTimeToLiveAsync(marker))!.Value.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1),
            "D2: the marker is kept long enough for any client's retry");
    }

    [Test]
    public async Task ASecondCheckoutMinutesLater_WithANewBasket_IsANewCheckout()
    {
        await using var factory = new BasketApiFactory();
        var first = factory.Catalog.Add("Mug", 12.50m);
        var second = factory.Catalog.Add("Pen", 0.99m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);

        await AddAsync(client, userId, first);
        var firstCheckout = await CheckOutAsync(client, userId);
        await AddAsync(client, userId, second);
        var secondCheckout = await CheckOutAsync(client, userId);

        secondCheckout.Should().NotBe(firstCheckout,
            "C2: this used to return the first checkout's id and leave the new basket unordered");
        var pending = await PendingAsync(factory);
        pending.Should().HaveCount(2);
        PayloadOf(pending[0]).GetProperty("items")[0].GetProperty("productId").GetGuid().Should().Be(second);
        (await factory.Redis.GetDatabase().KeyExistsAsync($"basket:user:{userId}")).Should().BeFalse();
    }

    [Test]
    public async Task ARetryOfACompletedCheckout_ReturnsTheSameCheckoutId_AndQueuesNothingMore()
    {
        await using var factory = new BasketApiFactory();
        var productId = factory.Catalog.Add("Mug", 12.50m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, productId);

        var checkoutId = await CheckOutAsync(client, userId);
        var retried = await CheckOutAsync(client, userId);

        retried.Should().Be(checkoutId, "D2: a request that finds the basket gone repeats the completed checkout");
        (await PendingAsync(factory)).Should().ContainSingle("a retry must never queue a second order (H2)");
    }

    [Test]
    public async Task ACheckout_WithNoBasketAndNoEarlierCheckout_IsRefused_AndQueuesNothing()
    {
        await using var factory = new BasketApiFactory();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);

        var response = await PostCheckoutAsync(client, userId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be("Basket.Empty");
        (await PendingAsync(factory)).Should().BeEmpty();
    }

    [Test]
    public async Task ABasketThatChangesWhileBeingCheckedOut_IsLeftExactlyAsItNowIs_AndNothingIsQueued()
    {
        await using var factory = new InterleavedWriteApiFactory();
        var original = factory.Catalog.Add("Mug", 12.50m);
        var addedMeanwhile = Guid.NewGuid();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, original);

        factory.AddToBasketAfterNextRead(userId, addedMeanwhile);
        var response = await PostCheckoutAsync(client, userId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ErrorCodeAsync(response)).Should().Be("Basket.CheckoutConflict");

        (await PendingAsync(factory)).Should().BeEmpty("a refused commit writes nothing — no event, so no order");
        var database = factory.Redis.GetDatabase();
        (await database.KeyExistsAsync($"basket:checkout:completed:{userId}")).Should().BeFalse();

        using var scope = factory.Services.CreateScope();
        var basket = await scope.ServiceProvider.GetRequiredService<IBasketRepository>().GetBasketAsync(userId);
        basket!.Items.Select(i => i.ProductId).Should().BeEquivalentTo(new[] { original, addedMeanwhile },
            "the item added mid-checkout must be neither ordered nor lost");
        (await database.SetContainsAsync($"basket:product:{original}:users", userId)).Should().BeTrue();
    }
}
