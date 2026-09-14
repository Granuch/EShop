using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.API.Infrastructure.Security;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Security;

/// <summary>
/// Who may use a basket (Basket audit S10: D10, L9, and the audit's note that the "allowed" tests asserted too little).
/// The owner may do everything; an admin may read any basket and change none but their own; the id must match exactly.
/// Before S10 an admin passed for every route, checkout included — an order for another user at an address the admin
/// chose — an id differing only in case passed too, and the allowed cases asserted only "not 401 or 403", which a 500
/// satisfies.
/// </summary>
[TestFixture]
[Category("Integration")]
public class BasketAuthorizationTests
{
    private BasketApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new BasketApiFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private async Task<(string UserId, Guid ProductId)> BasketWithAnItemAsync(string? userId = null)
    {
        userId ??= NewUser();
        var product = _factory.Catalog.Add("Mug", 10m);
        using var owner = _factory.CreateClientFor(userId);
        var response = await owner.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = product, quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return (userId, product);
    }

    private async Task<JsonElement> ReadAsOwnerAsync(string userId)
    {
        using var owner = _factory.CreateClientFor(userId);
        return await owner.GetFromJsonAsync<JsonElement>($"/api/v1/basket/{userId}");
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string write, string userId, Guid productId) => write switch
    {
        "add" => client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId, quantity = 1 }),
        "update" => client.PutAsJsonAsync($"/api/v1/basket/{userId}/items/{productId}", new { quantity = 5 }),
        "remove" => client.DeleteAsync($"/api/v1/basket/{userId}/items/{productId}"),
        "clear" => client.DeleteAsync($"/api/v1/basket/{userId}"),
        "checkout" => client.PostAsJsonAsync($"/api/v1/basket/{userId}/checkout", new
        {
            shippingAddress = new { street = "1 Main St", city = "Springfield", state = "IL", zipCode = "62701", country = "US" },
            paymentMethod = "Card"
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(write), write, null)
    };

    [Test]
    public async Task WithoutAToken_IsUnauthorized()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/basket/{NewUser()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AnotherUser_CannotReadTheBasket()
    {
        var (userId, _) = await BasketWithAnItemAsync();
        using var other = _factory.CreateClientFor(NewUser());

        var response = await other.GetAsync($"/api/v1/basket/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TheOwner_ReadsTheirBasket()
    {
        var (userId, product) = await BasketWithAnItemAsync();

        var basket = await ReadAsOwnerAsync(userId);

        basket.GetProperty("items")[0].GetProperty("productId").GetGuid().Should().Be(product);
    }

    /// <summary>D10: an admin may still look at any basket, for support.</summary>
    [Test]
    public async Task AnAdmin_ReadsAnotherUsersBasket()
    {
        var (userId, product) = await BasketWithAnItemAsync();
        using var admin = _factory.CreateClientFor(NewUser(), isAdmin: true);

        var response = await admin.GetAsync($"/api/v1/basket/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await response.Content.ReadFromJsonAsync<JsonElement>();
        basket.GetProperty("items")[0].GetProperty("productId").GetGuid().Should().Be(product);
    }

    /// <summary>D10: an admin changes no one else's basket, and above all does not check it out.</summary>
    [TestCase("add")]
    [TestCase("update")]
    [TestCase("remove")]
    [TestCase("clear")]
    [TestCase("checkout")]
    public async Task AnAdmin_CannotChangeAnotherUsersBasket(string write)
    {
        var (userId, product) = await BasketWithAnItemAsync();
        using var admin = _factory.CreateClientFor(NewUser(), isAdmin: true);

        var response = await SendAsync(admin, write, userId, product);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var basket = await ReadAsOwnerAsync(userId);
        basket.GetProperty("items").GetArrayLength().Should().Be(1, "the refused request must leave the basket as it was");
        basket.GetProperty("totalItems").GetInt64().Should().Be(1);
    }

    [Test]
    public async Task AnAdmin_StillOwnsTheirOwnBasket()
    {
        var adminId = NewUser();
        var product = _factory.Catalog.Add("Pen", 1m);
        using var admin = _factory.CreateClientFor(adminId, isAdmin: true);

        var response = await admin.PostAsJsonAsync($"/api/v1/basket/{adminId}/items", new { productId = product, quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// L9: the basket's keys are case-sensitive, so an id differing only in case is another basket. It used to pass the
    /// case-insensitive check and read and write a second, separate basket for the same user.
    /// </summary>
    [Test]
    public async Task AnIdDifferingOnlyInCase_IsSomeoneElse()
    {
        var (userId, product) = await BasketWithAnItemAsync();
        using var owner = _factory.CreateClientFor(userId);
        var otherCasing = userId.ToUpperInvariant();

        (await owner.GetAsync($"/api/v1/basket/{otherCasing}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(owner, "add", otherCasing, product)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Structural half, as Catalog's and Ordering's <c>NonAdminAuthorizationTests</c> do: every route under
    /// <c>/api/v1/basket/{userId}</c> carries the policy, so a new one cannot ship with a looser one or none.
    /// </summary>
    [Test]
    public void EveryBasketRoute_RequiresTheOwnerOrAdminReadPolicy()
    {
        var routes = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/basket/{userId}", StringComparison.Ordinal) == true)
            .ToList();

        routes.Should().HaveCount(6);
        routes.Should().OnlyContain(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy == OwnerOrAdminReadRequirement.PolicyName));
    }
}
