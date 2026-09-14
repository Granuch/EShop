using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Persistence;

/// <summary>
/// Basket audit S2. <c>RedisBasketRepository</c> against real Redis: the document round trip, the basket's TTL and
/// the <c>basket:product:{id}:users</c> reverse index that price sync fans out over. The unit tests of the same
/// class run against a mocked <c>IDatabase</c>, which returns whatever it is told to and so cannot show what the
/// transaction actually leaves behind.
///
/// <para>One host per fixture; every test uses its own user and products, so they cannot see each other's keys.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class RedisBasketRepositoryTests
{
    private static readonly TimeSpan BasketTtl = TimeSpan.FromDays(7);

    private BasketApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new BasketApiFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    private IDatabase Database => _factory.Redis.GetDatabase();

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static string IndexKey(Guid productId) => $"basket:product:{productId}:users";

    private async Task<T> WithRepository<T>(Func<IBasketRepository, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IBasketRepository>());
    }

    [Test]
    public async Task ASavedBasket_ReadsBackLineForLine()
    {
        var userId = NewUser();
        var mug = Guid.NewGuid();
        var pen = Guid.NewGuid();
        var basket = ShoppingBasket.Create(userId);
        basket.AddItem(mug, "Mug", 12.50m, 2);
        basket.AddItem(pen, "Pen", 0.99m, 10);

        await WithRepository(repository => repository.SaveBasketAsync(basket));
        var read = await WithRepository(repository => repository.GetBasketAsync(userId));

        read.Should().NotBeNull();
        read!.UserId.Should().Be(userId);
        read.Items.Select(i => (i.ProductId, i.ProductName, i.Price, i.Quantity)).Should().BeEquivalentTo(new[]
        {
            (mug, "Mug", 12.50m, 2),
            (pen, "Pen", 0.99m, 10)
        });
    }

    [Test]
    public async Task ASavedBasket_ExpiresAfterTheConfiguredTtl()
    {
        var userId = NewUser();
        var basket = ShoppingBasket.Create(userId);
        basket.AddItem(Guid.NewGuid(), "Mug", 12.50m, 1);

        await WithRepository(repository => repository.SaveBasketAsync(basket));

        var ttl = await Database.KeyTimeToLiveAsync($"basket:user:{userId}");
        ttl.Should().NotBeNull("a basket without a TTL would live in Redis forever");
        ttl!.Value.Should().BeCloseTo(BasketTtl, TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task Saving_IndexesEveryProduct_AndUnindexesOneThatWasRemoved()
    {
        var userId = NewUser();
        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var basket = ShoppingBasket.Create(userId);
        basket.AddItem(kept, "Kept", 1m, 1);
        basket.AddItem(removed, "Removed", 1m, 1);
        await WithRepository(repository => repository.SaveBasketAsync(basket));

        (await Database.SetContainsAsync(IndexKey(kept), userId)).Should().BeTrue();
        (await Database.SetContainsAsync(IndexKey(removed), userId)).Should().BeTrue();

        basket.RemoveItem(removed);
        await WithRepository(repository => repository.SaveBasketAsync(basket));

        (await Database.SetContainsAsync(IndexKey(kept), userId)).Should().BeTrue();
        (await Database.SetContainsAsync(IndexKey(removed), userId)).Should().BeFalse(
            "a stale entry sends every future price change for that product to a basket that no longer holds it");
        (await WithRepository(repository => repository.GetUsersContainingProductAsync(kept))).Should().Contain(userId);
        (await Database.KeyTimeToLiveAsync(IndexKey(kept))).Should().NotBeNull("the index set expires with the baskets");
    }

    [Test]
    public async Task Deleting_RemovesTheBasketAndItsIndexEntries()
    {
        var userId = NewUser();
        var product = Guid.NewGuid();
        var basket = ShoppingBasket.Create(userId);
        basket.AddItem(product, "Mug", 12.50m, 1);
        await WithRepository(repository => repository.SaveBasketAsync(basket));

        (await WithRepository(repository => repository.DeleteBasketAsync(userId))).Should().BeTrue();

        (await WithRepository(repository => repository.GetBasketAsync(userId))).Should().BeNull();
        (await Database.KeyExistsAsync($"basket:user:{userId}")).Should().BeFalse();
        (await Database.SetContainsAsync(IndexKey(product), userId)).Should().BeFalse();
        (await WithRepository(repository => repository.DeleteBasketAsync(userId))).Should().BeFalse(
            "deleting a basket that is already gone reports that nothing was deleted");
    }
}
