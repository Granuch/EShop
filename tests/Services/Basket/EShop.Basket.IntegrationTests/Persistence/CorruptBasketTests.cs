using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Persistence;

/// <summary>
/// Basket audit S9 (L6), on real Redis. An unreadable basket document — not JSON, another user's, or holding a line the
/// domain refuses — reads as an empty basket, as before. The write that replaces or deletes it now keeps it under
/// <c>basket:corrupt:{userId}</c>; it used to be overwritten with only a warning to say it had existed.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CorruptBasketTests
{
    private BasketApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new BasketApiFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    private IDatabase Database => _factory.Redis.GetDatabase();

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static string BasketKey(string userId) => $"basket:user:{userId}";

    private static string CorruptKey(string userId) => $"basket:corrupt:{userId}";

    private static string ForeignDocument() => JsonSerializer.Serialize(new
    {
        userId = "someone-else",
        items = new[] { new { productId = Guid.NewGuid(), productName = "Theirs", price = 1m, quantity = 1 } },
        createdAt = DateTime.UtcNow,
        lastModifiedAt = DateTime.UtcNow
    });

    private static string DocumentWithARefusedLine(string userId) => JsonSerializer.Serialize(new
    {
        userId,
        items = new[] { new { productId = Guid.NewGuid(), productName = "Nothing of it", price = 1m, quantity = 0 } },
        createdAt = DateTime.UtcNow,
        lastModifiedAt = DateTime.UtcNow
    });

    private async Task AddAsync(string userId)
    {
        var product = _factory.Catalog.Add("Mug", 10m);
        using var client = _factory.CreateClientFor(userId);
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = product, quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task AnUnreadableBasket_IsKept_WhenTheNextAddReplacesIt()
    {
        var userId = NewUser();
        const string unreadable = "{\"userId\": not json";
        await Database.StringSetAsync(BasketKey(userId), unreadable);

        await AddAsync(userId);

        (await Database.ListRangeAsync(CorruptKey(userId))).Select(v => v.ToString())
            .Should().Equal(new[] { unreadable }, "the add replaced it; it used to be gone without a trace");
        (await Database.KeyTimeToLiveAsync(CorruptKey(userId))).Should().NotBeNull("kept for a while, not forever");
        ((string?)await Database.StringGetAsync(BasketKey(userId))).Should().Contain("Mug");
    }

    [Test]
    public async Task AnotherUsersBasket_IsKept_WhenTheBasketIsCleared()
    {
        var userId = NewUser();
        var foreign = ForeignDocument();
        await Database.StringSetAsync(BasketKey(userId), foreign);

        using var client = _factory.CreateClientFor(userId);
        var response = await client.DeleteAsync($"/api/v1/basket/{userId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Database.KeyExistsAsync(BasketKey(userId))).Should().BeFalse();
        (await Database.ListRangeAsync(CorruptKey(userId))).Select(v => v.ToString()).Should().Equal(foreign);
    }

    /// <summary>
    /// A line the domain refuses used to throw from every read, so that user's GET and every add failed until the basket
    /// expired. It now reads as no basket, and the next add keeps it and starts afresh.
    /// </summary>
    [Test]
    public async Task ABasketWithALineTheDomainRefuses_ReadsAsEmpty_AndIsKeptWhenReplaced()
    {
        var userId = NewUser();
        var refused = DocumentWithARefusedLine(userId);
        await Database.StringSetAsync(BasketKey(userId), refused);
        using var client = _factory.CreateClientFor(userId);

        var read = await client.GetAsync($"/api/v1/basket/{userId}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(0);

        await AddAsync(userId);

        (await Database.ListRangeAsync(CorruptKey(userId))).Select(v => v.ToString()).Should().Equal(refused);
    }

    [Test]
    public async Task ReadingAnUnreadableBasket_LeavesItWhereItIs()
    {
        var userId = NewUser();
        await Database.StringSetAsync(BasketKey(userId), "not json");
        using var client = _factory.CreateClientFor(userId);

        var response = await client.GetAsync($"/api/v1/basket/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ((string?)await Database.StringGetAsync(BasketKey(userId))).Should().Be("not json", "only a write moves it aside");
        (await Database.KeyExistsAsync(CorruptKey(userId))).Should().BeFalse();
    }
}
