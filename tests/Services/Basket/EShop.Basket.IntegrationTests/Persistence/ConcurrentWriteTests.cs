using System.Net;
using System.Net.Http.Json;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Consumers;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Messaging.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EShop.Basket.IntegrationTests.Persistence;

/// <summary>
/// Basket audit S4 (H3, M8, M1), on real Redis. Each race is landed deterministically by
/// <see cref="InterleavedWriteApiFactory"/>: the competing write is stored right after the write under test has read
/// the basket, so the write under test holds a stale basket. Before S4 each of these lost the other write — an item, a
/// price, an index entry — while every request still answered success.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ConcurrentWriteTests
{
    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static async Task AddAsync(HttpClient client, string userId, Guid productId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId, quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private static async Task<ShoppingBasket?> StoredAsync(BasketApiFactory factory, string userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBasketRepository>().GetBasketAsync(userId);
    }

    private static Task<bool> IndexedAsync(BasketApiFactory factory, Guid productId, string userId)
        => factory.Redis.GetDatabase().SetContainsAsync($"basket:product:{productId}:users", userId);

    [Test]
    public async Task AnItemAddedWhileAnotherAddIsBeingSaved_IsKept()
    {
        await using var factory = new InterleavedWriteApiFactory();
        var first = factory.Catalog.Add("Mug", 12.50m);
        var second = factory.Catalog.Add("Pen", 0.99m);
        var fromOtherTab = Guid.NewGuid();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, first);

        factory.AddToBasketAfterNextRead(userId, fromOtherTab);
        await AddAsync(client, userId, second);

        (await StoredAsync(factory, userId))!.Items.Select(i => i.ProductId)
            .Should().BeEquivalentTo(new[] { first, second, fromOtherTab }, "two tabs adding at once must both keep their item");
        (await IndexedAsync(factory, fromOtherTab, userId)).Should().BeTrue();
    }

    [Test]
    public async Task APriceSyncLandingDuringAQuantityEdit_IsNotUndone()
    {
        await using var factory = new InterleavedWriteApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug);

        factory.AfterNextRead(userId, basket => basket.ApplyPriceChange(mug, 15m));
        var response = await client.PutAsJsonAsync($"/api/v1/basket/{userId}/items/{mug}", new { quantity = 3 });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var item = (await StoredAsync(factory, userId))!.Items.Single();
        item.Quantity.Should().Be(3);
        item.Price.Should().Be(15m,
            "the edit used to save the price it had read, undoing the sync, and the event never comes again");
    }

    [Test]
    public async Task AnItemAddedDuringAPriceSync_IsKept_AndTheNewPriceStillApplied()
    {
        await using var factory = new InterleavedWriteApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var addedDuringSync = Guid.NewGuid();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug);

        factory.AddToBasketAfterNextRead(userId, addedDuringSync);
        await PriceSync.RunAsync(factory,mug, 15m);

        var basket = await StoredAsync(factory, userId);
        basket!.Items.Select(i => i.ProductId).Should().BeEquivalentTo(new[] { mug, addedDuringSync },
            "the sync used to save the basket it had read and drop the item the customer had just added");
        basket.Items.Single(i => i.ProductId == mug).Price.Should().Be(15m);
    }

    [Test]
    public async Task RemovingAnItemWhileAnotherIsAdded_KeepsTheBasketAndTheIndexRight()
    {
        await using var factory = new InterleavedWriteApiFactory();
        var removed = factory.Catalog.Add("Mug", 10m);
        var kept = factory.Catalog.Add("Pen", 1m);
        var addedMeanwhile = Guid.NewGuid();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, removed);
        await AddAsync(client, userId, kept);

        factory.AddToBasketAfterNextRead(userId, addedMeanwhile);
        var response = await client.DeleteAsync($"/api/v1/basket/{userId}/items/{removed}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StoredAsync(factory, userId))!.Items.Select(i => i.ProductId)
            .Should().BeEquivalentTo(new[] { kept, addedMeanwhile });
        (await IndexedAsync(factory, removed, userId)).Should().BeFalse();
        (await IndexedAsync(factory, kept, userId)).Should().BeTrue();
        (await IndexedAsync(factory, addedMeanwhile, userId)).Should().BeTrue();
    }

    [Test]
    public async Task RemovingTheLastItemWhileAnotherIsAdded_KeepsTheBasketWithTheOtherItem()
    {
        await using var factory = new InterleavedWriteApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var addedMeanwhile = Guid.NewGuid();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug);

        // The remove reads a basket holding only the mug, so it looks like the last item and the basket is deleted —
        // conditionally. The delete used to be unconditional and took the item added meanwhile with it.
        factory.AddToBasketAfterNextRead(userId, addedMeanwhile);
        var response = await client.DeleteAsync($"/api/v1/basket/{userId}/items/{mug}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StoredAsync(factory, userId))!.Items.Single().ProductId.Should().Be(addedMeanwhile);
        (await IndexedAsync(factory, mug, userId)).Should().BeFalse();
        (await IndexedAsync(factory, addedMeanwhile, userId)).Should().BeTrue();
    }

    [Test]
    public async Task APriceSync_DropsUsersTheIndexListsButWhoseBasketNoLongerHoldsTheProduct()
    {
        await using var factory = new BasketApiFactory();
        var product = factory.Catalog.Add("Mug", 10m);
        var other = factory.Catalog.Add("Pen", 1m);
        var withoutTheProduct = NewUser();
        var withoutABasket = NewUser();
        using var client = factory.CreateClientFor(withoutTheProduct);
        await AddAsync(client, withoutTheProduct, other);

        var database = factory.Redis.GetDatabase();
        await database.SetAddAsync($"basket:product:{product}:users", withoutTheProduct);
        await database.SetAddAsync($"basket:product:{product}:users", withoutABasket);

        await PriceSync.RunAsync(factory,product, 15m);

        (await IndexedAsync(factory, product, withoutTheProduct)).Should().BeFalse();
        (await IndexedAsync(factory, product, withoutABasket)).Should().BeFalse();
        (await StoredAsync(factory, withoutTheProduct))!.Items.Single().ProductId.Should().Be(other,
            "dropping a stale index entry must not touch the basket");
    }

    [Test]
    public async Task AddingAProductAgain_TakesItsCurrentCatalogNameAndPrice()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        await AddAsync(client, userId, mug);

        factory.Catalog.Update(mug, "Mug (new edition)", 8m);
        await AddAsync(client, userId, mug);

        var item = (await StoredAsync(factory, userId))!.Items.Single();
        item.Quantity.Should().Be(2);
        item.Price.Should().Be(8m, "M1: a missed price event used to survive every re-add");
        item.ProductName.Should().Be("Mug (new edition)");
    }
}
