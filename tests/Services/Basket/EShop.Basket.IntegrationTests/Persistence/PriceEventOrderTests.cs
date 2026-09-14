using System.Net;
using System.Net.Http.Json;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Persistence;

/// <summary>
/// Basket audit S9 (M10, M9), on real Redis through the real consumer. Before S9 the consumer ignored when a change
/// happened, so an older event applied after a newer one left every basket at the older price; and it re-priced one
/// basket at a time.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PriceEventOrderTests
{
    private static readonly DateTime Earlier = DateTime.UtcNow.AddMinutes(-10);
    private static readonly DateTime Later = DateTime.UtcNow.AddMinutes(-5);

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static async Task<string> BasketHoldingAsync(BasketApiFactory factory, Guid productId)
    {
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId, quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return userId;
    }

    private static async Task<decimal> PriceAsync(BasketApiFactory factory, string userId, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        var basket = await scope.ServiceProvider.GetRequiredService<IBasketRepository>().GetBasketAsync(userId);
        return basket!.Items.Single(i => i.ProductId == productId).Price;
    }

    [Test]
    public async Task AnOlderPriceChange_ArrivingAfterANewerOne_DoesNotPutTheOlderPriceBack()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = await BasketHoldingAsync(factory, mug);

        await PriceSync.RunAsync(factory, mug, 15m, Later);
        await PriceSync.RunAsync(factory, mug, 12m, Earlier);

        (await PriceAsync(factory, userId, mug)).Should().Be(15m,
            "the 12 was the price before the 15; applying it last left the basket at a price Catalog no longer has");
    }

    [Test]
    public async Task ANewerPriceChange_StillApplies_AfterAnOlderOne()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = await BasketHoldingAsync(factory, mug);

        await PriceSync.RunAsync(factory, mug, 12m, Earlier);
        await PriceSync.RunAsync(factory, mug, 15m, Later);

        (await PriceAsync(factory, userId, mug)).Should().Be(15m);
    }

    /// <summary>
    /// The older event reads the basket first; the newer one runs completely right after that read. The older event's
    /// save then fails and it re-reads — and without the check after each read it would apply its older price on top.
    /// </summary>
    [Test]
    public async Task AnOlderPriceChange_RacingANewerOne_LosesEvenThoughItReadTheBasketFirst()
    {
        await using var factory = new InterleavedWriteApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = await BasketHoldingAsync(factory, mug);

        factory.RunAfterNextRead(userId, () => PriceSync.RunAsync(factory, mug, 15m, Later));
        await PriceSync.RunAsync(factory, mug, 12m, Earlier);

        (await PriceAsync(factory, userId, mug)).Should().Be(15m);
    }

    [Test]
    public async Task APriceChange_RepricesEveryBasketThatHoldsTheProduct()
    {
        await using var factory = new BasketApiFactory();
        var mug = Guid.NewGuid();
        var users = Enumerable.Range(0, 40).Select(_ => NewUser()).ToArray();

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBasketRepository>();
            foreach (var userId in users)
            {
                var basket = ShoppingBasket.Create(userId);
                basket.AddItem(mug, "Mug", 10m, 1);
                (await repository.TrySaveBasketAsync(basket)).Should().BeTrue();
            }
        }

        await PriceSync.RunAsync(factory, mug, 15m);

        foreach (var userId in users)
        {
            (await PriceAsync(factory, userId, mug)).Should().Be(15m, $"{userId} holds the product");
        }
    }
}
