using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Api;

/// <summary>
/// Basket audit S9 (M2, D9). A line holds at most <see cref="ShoppingBasket.MaxQuantityPerLine"/> and a basket at most
/// <see cref="ShoppingBasket.MaxLines"/> products; before S9 neither had a bound, and two large lines overflowed the
/// basket's item count so that its GET failed for good.
/// </summary>
[TestFixture]
[Category("Integration")]
public class BasketLimitTests
{
    private BasketApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new BasketApiFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString();

    [Test]
    public async Task AddingMoreThanALineCanHold_Is400()
    {
        var product = _factory.Catalog.Add("Mug", 10m, stock: 5000);
        var userId = NewUser();
        using var client = _factory.CreateClientFor(userId);

        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items",
            new { productId = product, quantity = ShoppingBasket.MaxQuantityPerLine + 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be("Validation.Failed");
    }

    [Test]
    public async Task AddingToALineUntilItHoldsTooMany_Is400_AndLeavesTheLine()
    {
        var product = _factory.Catalog.Add("Mug", 10m, stock: 5000);
        var userId = NewUser();
        using var client = _factory.CreateClientFor(userId);
        (await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items",
            new { productId = product, quantity = ShoppingBasket.MaxQuantityPerLine })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = product, quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be("Basket.ValidationFailed");
        var basket = await (await client.GetAsync($"/api/v1/basket/{userId}")).Content.ReadFromJsonAsync<JsonElement>();
        basket.GetProperty("totalItems").GetInt64().Should().Be(ShoppingBasket.MaxQuantityPerLine);
    }

    [Test]
    public async Task AddingOneProductTooMany_Is400()
    {
        var userId = NewUser();
        using (var scope = _factory.Services.CreateScope())
        {
            var basket = ShoppingBasket.Create(userId);
            for (var i = 0; i < ShoppingBasket.MaxLines; i++)
            {
                basket.AddItem(Guid.NewGuid(), $"Product {i}", 1m, 1);
            }

            (await scope.ServiceProvider.GetRequiredService<IBasketRepository>().TrySaveBasketAsync(basket)).Should().BeTrue();
        }

        var product = _factory.Catalog.Add("One too many", 1m);
        using var client = _factory.CreateClientFor(userId);
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = product, quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be("Basket.ValidationFailed");
    }

    [Test]
    public async Task SettingAQuantityAboveTheLimit_Is400()
    {
        var product = _factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = _factory.CreateClientFor(userId);
        (await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items",
            new { productId = product, quantity = 1 })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync($"/api/v1/basket/{userId}/items/{product}",
            new { quantity = ShoppingBasket.MaxQuantityPerLine + 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Two lines of 1.5 billion, as a basket stored before the limit could hold; its GET used to fail for good.</summary>
    [Test]
    public async Task ABasketStoredBeforeTheLimits_StillReads()
    {
        var userId = NewUser();
        await _factory.Redis.GetDatabase().StringSetAsync($"basket:user:{userId}", JsonSerializer.Serialize(new
        {
            userId,
            items = new[]
            {
                new { productId = Guid.NewGuid(), productName = "A", price = 1m, quantity = 1_500_000_000 },
                new { productId = Guid.NewGuid(), productName = "B", price = 1m, quantity = 1_500_000_000 }
            },
            createdAt = DateTime.UtcNow,
            lastModifiedAt = DateTime.UtcNow
        }));
        using var client = _factory.CreateClientFor(userId);

        var response = await client.GetAsync($"/api/v1/basket/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalItems").GetInt64().Should().Be(3_000_000_000L);
    }
}
