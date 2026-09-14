using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Checkout;

/// <summary>
/// Basket audit S6 (H5, D6), end to end on real Redis. Catalog was read once per item, when it was added, so a product
/// deleted, unpublished, sold out or repriced afterwards still checked out at its stored price — and Ordering trusts
/// the prices Basket sends. Checkout now re-reads every line and refuses with a 409 listing each line and why; a
/// repriced basket takes the new price, so the next checkout orders at a price the customer has seen.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CheckoutRevalidationTests
{
    private const string PendingOutbox = "basket:outbox:pending";

    private static readonly object CheckoutBody = new
    {
        shippingAddress = new { street = "1 Main St", city = "Springfield", state = "IL", zipCode = "62701", country = "US" },
        paymentMethod = "card"
    };

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static async Task AddAsync(HttpClient client, string userId, Guid productId, int quantity = 1)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId, quantity });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> CheckOutAsync(HttpClient client, string userId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/checkout", CheckoutBody);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, body.RootElement.Clone());
    }

    private static async Task<long> PendingCountAsync(BasketApiFactory factory)
        => await factory.Redis.GetDatabase().ListLengthAsync(PendingOutbox);

    private static async Task<decimal> StoredPriceAsync(BasketApiFactory factory, string userId, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        var basket = await scope.ServiceProvider.GetRequiredService<IBasketRepository>().GetBasketAsync(userId);
        return basket!.Items.Single(i => i.ProductId == productId).Price;
    }

    private static JsonElement TheOnlyLine(JsonElement problem)
    {
        problem.GetProperty("errorCode").GetString().Should().Be("Basket.CheckoutRevalidationFailed");
        var lines = problem.GetProperty("lines");
        lines.GetArrayLength().Should().Be(1);
        return lines[0];
    }

    [Test]
    public async Task ARepricedProduct_RefusesTheCheckout_AndTheNextCheckoutOrdersAtTheNewPrice()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug, quantity: 2);

        factory.Catalog.SetPrice(mug, 12m);
        var (status, problem) = await CheckOutAsync(client, userId);

        status.Should().Be(HttpStatusCode.Conflict);
        var line = TheOnlyLine(problem);
        line.GetProperty("productId").GetGuid().Should().Be(mug);
        line.GetProperty("reason").GetString().Should().Be("Repriced");
        line.GetProperty("basketPrice").GetDecimal().Should().Be(10m);
        line.GetProperty("catalogPrice").GetDecimal().Should().Be(12m);
        (await PendingCountAsync(factory)).Should().Be(0, "nothing is ordered at a price the customer did not see");
        (await StoredPriceAsync(factory, userId, mug)).Should().Be(12m, "D6: the basket takes Catalog's price");

        var (secondStatus, _) = await CheckOutAsync(client, userId);

        secondStatus.Should().Be(HttpStatusCode.OK);
        var envelope = JsonDocument.Parse((string)(await factory.Redis.GetDatabase().ListGetByIndexAsync(PendingOutbox, 0))!).RootElement;
        var payload = JsonDocument.Parse(envelope.GetProperty("payload").GetString()!).RootElement;
        payload.GetProperty("items")[0].GetProperty("price").GetDecimal().Should().Be(12m);
        payload.GetProperty("totalPrice").GetDecimal().Should().Be(24m);
    }

    [Test]
    public async Task AProductNoLongerInThePublicCatalog_RefusesTheCheckoutAsUnavailable()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug);

        factory.Catalog.Remove(mug);
        var (status, problem) = await CheckOutAsync(client, userId);

        status.Should().Be(HttpStatusCode.Conflict);
        var line = TheOnlyLine(problem);
        line.GetProperty("reason").GetString().Should().Be("Unavailable");
        line.GetProperty("catalogPrice").ValueKind.Should().Be(JsonValueKind.Null);
        (await PendingCountAsync(factory)).Should().Be(0);
        (await StoredPriceAsync(factory, userId, mug)).Should().Be(10m, "the customer decides what to do with the line");
    }

    [Test]
    public async Task ALineBeyondWhatIsInStock_RefusesTheCheckoutAsOutOfStock()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m, stock: 5);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug, quantity: 3);

        factory.Catalog.SetStock(mug, 2);
        var (status, problem) = await CheckOutAsync(client, userId);

        status.Should().Be(HttpStatusCode.Conflict);
        var line = TheOnlyLine(problem);
        line.GetProperty("reason").GetString().Should().Be("OutOfStock");
        line.GetProperty("requestedQuantity").GetInt32().Should().Be(3);
        line.GetProperty("availableQuantity").GetInt32().Should().Be(2);
        (await PendingCountAsync(factory)).Should().Be(0);
    }

    [Test]
    public async Task WhenCatalogCannotBeReached_NothingIsCheckedOut()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug);

        factory.Catalog.IsUnavailable = true;
        var (status, problem) = await CheckOutAsync(client, userId);

        status.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("errorCode").GetString().Should().Be("Basket.ProductVerificationFailed");
        (await PendingCountAsync(factory)).Should().Be(0, "an unverified basket must not become an order");
        (await factory.Redis.GetDatabase().KeyExistsAsync($"basket:user:{userId}")).Should().BeTrue();
    }

    [Test]
    public async Task AddingMoreThanIsInStock_IsRefused()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m, stock: 3);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug, quantity: 2);

        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = mug, quantity = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("Basket.InsufficientStock");
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IBasketRepository>().GetBasketAsync(userId))!
            .Items.Single().Quantity.Should().Be(2, "2 already in the basket plus 2 more is beyond the 3 in stock");
    }
}
