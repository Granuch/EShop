using System.Net.Http.Json;
using EShop.Basket.Application.Queries.GetBasket;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Application;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Persistence;

/// <summary>
/// Basket audit S5 (H4, D5). <c>GET /basket</c> used to be served from a two-minute Redis copy of the basket that the
/// price-sync consumer never evicted, so a customer saw an old price that checkout no longer charged. The cache is
/// gone; these pin both the behaviour and the wiring, since re-adding one <c>ICacheableQuery</c> would bring it back.
/// </summary>
[TestFixture]
[Category("Integration")]
public class NoBasketCacheTests
{
    [Test]
    public async Task AGetRightAfterAPriceSync_ShowsTheNewPrice()
    {
        await using var factory = new BasketApiFactory();
        var mug = factory.Catalog.Add("Mug", 10m);
        var userId = $"user-{Guid.NewGuid():N}";
        using var client = factory.CreateClientFor(userId);
        (await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = mug, quantity = 1 }))
            .EnsureSuccessStatusCode();

        // Read first, so any cache there were would now hold the old price.
        (await client.GetFromJsonAsync<BasketDto>($"/api/v1/basket/{userId}"))!.Items.Single().Price.Should().Be(10m);

        await PriceSync.RunAsync(factory, mug, 15m);

        (await client.GetFromJsonAsync<BasketDto>($"/api/v1/basket/{userId}"))!.Items.Single().Price.Should().Be(15m,
            "GET must show the price checkout will charge, not a copy the price sync never evicted");
        (await factory.Redis.GetDatabase().KeyExistsAsync($"EShop_Basket_basket:v1:basket:user:{userId}"))
            .Should().BeFalse("no second copy of the basket is written any more");
    }

    [Test]
    public void TheBasketQuery_RunsThroughValidationAndLoggingOnly()
    {
        using var factory = new BasketApiFactory();
        using var scope = factory.Services.CreateScope();

        var behaviors = scope.ServiceProvider
            .GetServices<IPipelineBehavior<GetBasketQuery, Result<BasketDto>>>()
            .Select(behavior => behavior.GetType().Name)
            .ToList();

        behaviors.Should().Equal(new[] { "ValidationBehavior`2", "LoggingBehavior`2" },
            "Basket has no transaction and, since S5, no caching or cache-invalidation behavior");
        scope.ServiceProvider.GetService<IDistributedCache>().Should().BeNull(
            "nothing in Basket should be caching baskets in a second store");
    }
}
