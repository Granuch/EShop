using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;

namespace EShop.Basket.IntegrationTests.Api;

/// <summary>
/// Basket audit S8 (M3, M4, L5; D8). What each kind of failure reaches the client as, now that one mapping serves every
/// endpoint: something missing is 404, an outage is 503, and a user with no basket gets an empty one.
/// </summary>
[TestFixture]
[Category("Integration")]
public class StatusCodeTests
{
    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static async Task<(HttpStatusCode Status, string? ErrorCode)> ProblemOf(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
        {
            return (response.StatusCode, null);
        }

        using var json = JsonDocument.Parse(body);
        return (response.StatusCode,
            json.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null);
    }

    [Test]
    public async Task AUserWithNoBasket_GetsAnEmptyBasket_NotA404()
    {
        await using var factory = new BasketApiFactory();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);

        var response = await client.GetAsync($"/api/v1/basket/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "D8: a basket UI should not have to treat a new user as an error");
        var basket = await response.Content.ReadFromJsonAsync<JsonElement>();
        basket.GetProperty("userId").GetString().Should().Be(userId);
        basket.GetProperty("items").GetArrayLength().Should().Be(0);
        basket.GetProperty("totalPrice").GetDecimal().Should().Be(0m);
        basket.GetProperty("totalItems").GetInt32().Should().Be(0);
        basket.GetProperty("createdAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task ChangingTheQuantityOfAProductNotInTheBasket_Is404ItemNotFound()
    {
        await using var factory = new BasketApiFactory();
        var inBasket = factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        (await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = inBasket, quantity = 1 }))
            .EnsureSuccessStatusCode();

        var (status, code) = await ProblemOf(
            client.PutAsJsonAsync($"/api/v1/basket/{userId}/items/{Guid.NewGuid()}", new { quantity = 2 }));

        status.Should().Be(HttpStatusCode.NotFound, "M4: this was a DomainException reported as 400 and logged as an error");
        code.Should().Be("Basket.ItemNotFound");
    }

    [Test]
    public async Task ChangingAQuantityWithNoBasket_Is404BasketNotFound()
    {
        await using var factory = new BasketApiFactory();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);

        var (status, code) = await ProblemOf(
            client.PutAsJsonAsync($"/api/v1/basket/{userId}/items/{Guid.NewGuid()}", new { quantity = 2 }));

        status.Should().Be(HttpStatusCode.NotFound);
        code.Should().Be("Basket.NotFound");
    }

    [Test]
    public async Task AddingAProductCatalogDoesNotHave_Is404ProductNotFound()
    {
        await using var factory = new BasketApiFactory();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);

        var (status, code) = await ProblemOf(
            client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = Guid.NewGuid(), quantity = 1 }));

        status.Should().Be(HttpStatusCode.NotFound, "M4: the same missing product used to be 400 here and 404 elsewhere");
        code.Should().Be("Basket.ProductNotFound");
    }

    [TestCase(false, TestName = "CatalogDown")]
    [TestCase(true, TestName = "CatalogTimingOut")]
    public async Task AddingWhileCatalogCannotAnswer_Is503ProductVerificationFailed(bool timesOut)
    {
        await using var factory = new BasketApiFactory();
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        factory.Catalog.IsUnavailable = !timesOut;
        factory.Catalog.TimesOut = timesOut;

        var (status, code) = await ProblemOf(
            client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = Guid.NewGuid(), quantity = 1 }));

        status.Should().Be(HttpStatusCode.ServiceUnavailable, "M3: an outage used to reach the client as a 400");
        code.Should().Be("Basket.ProductVerificationFailed",
            "L5: an HttpClient timeout is a TaskCanceledException, which used to fall into the generic branch");
    }

    [Test]
    public async Task WhileRedisIsDown_EveryBasketEndpointAnswers503()
    {
        await using var factory = new RedisDownApiFactory();
        var product = factory.Catalog.Add("Mug", 10m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId);
        var basketUrl = $"/api/v1/basket/{userId}";

        var calls = new (string Name, Task<HttpResponseMessage> Call)[]
        {
            ("GET", client.GetAsync(basketUrl)),
            ("POST items", client.PostAsJsonAsync($"{basketUrl}/items", new { productId = product, quantity = 1 })),
            ("PUT item", client.PutAsJsonAsync($"{basketUrl}/items/{product}", new { quantity = 2 })),
            ("DELETE item", client.DeleteAsync($"{basketUrl}/items/{product}")),
            ("DELETE basket", client.DeleteAsync(basketUrl))
        };

        foreach (var (name, call) in calls)
        {
            var (status, code) = await ProblemOf(call);
            status.Should().Be(HttpStatusCode.ServiceUnavailable, $"{name} must report an outage as one");
            code.Should().Be("Basket.OperationFailed", name);
        }
    }
}
