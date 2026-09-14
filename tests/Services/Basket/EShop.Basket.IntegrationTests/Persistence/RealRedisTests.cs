using System.Net;
using System.Net.Http.Json;
using EShop.Basket.Application.Queries.GetBasket;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Persistence;

/// <summary>
/// Basket audit S2. The first tests in this suite that reach Redis at all: until S2 every one ran against a Moq
/// multiplexer that answered every read with "no basket".
///
/// <para>The sentinel exists only to go red if the suite ever falls back to a stub, which is otherwise invisible:
/// the auth, binding and validation tests pass either way. It checks the host's multiplexer is the real type and
/// that what it writes can be read by an independent connection to the same database.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class RealRedisTests
{
    [Test]
    public async Task TheHostStoresBasketsInARealRedisServer()
    {
        await using var factory = new BasketApiFactory();

        var redis = factory.Redis;
        redis.Should().BeOfType<ConnectionMultiplexer>("a stubbed multiplexer would make every Redis test meaningless");

        await redis.GetDatabase().StringSetAsync("sentinel", "written-by-the-host");

        await using var independent = await ConnectionMultiplexer.ConnectAsync(factory.RedisConnectionString);
        ((string?)await independent.GetDatabase().StringGetAsync("sentinel")).Should().Be("written-by-the-host");
    }

    [Test]
    public async Task AnItemAddedOverHttp_IsStoredInRedis_AndReadBackAtItsCatalogPrice()
    {
        await using var factory = new BasketApiFactory();
        var productId = factory.Catalog.Add("Real Redis Mug", 12.50m);
        var userId = $"user-{Guid.NewGuid():N}";
        using var client = factory.CreateClientFor(userId);

        var add = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId, quantity = 2 });
        add.StatusCode.Should().Be(HttpStatusCode.NoContent, await add.Content.ReadAsStringAsync());

        var get = await client.GetAsync($"/api/v1/basket/{userId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await get.Content.ReadFromJsonAsync<BasketDto>();

        basket!.Items.Should().ContainSingle();
        basket.Items[0].ProductId.Should().Be(productId);
        basket.Items[0].ProductName.Should().Be("Real Redis Mug");
        basket.Items[0].Price.Should().Be(12.50m);
        basket.Items[0].Quantity.Should().Be(2);
        basket.TotalPrice.Should().Be(25.00m);

        var database = factory.Redis.GetDatabase();
        (await database.KeyExistsAsync($"basket:user:{userId}")).Should().BeTrue();
        (await database.SetContainsAsync($"basket:product:{productId}:users", userId)).Should().BeTrue(
            "price sync finds baskets through this index, so a basket missing from it never gets re-priced");
    }
}
