using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;
using FluentAssertions;

namespace EShop.Basket.IntegrationTests.Api;

/// <summary>
/// Basket audit S11 (M6/D11, L2, L3). The request and response shapes, through the real host and its OpenAPI document:
/// checkout needs no payment method; an add's client-side name and price are gone (and ignored if still sent); checkout
/// returns a declared <c>CheckoutResponse</c>. Before S11 a checkout without <c>paymentMethod</c> was a 400, the OpenAPI
/// document advertised client-side pricing, and the checkout response had no schema at all.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RequestContractTests
{
    private static readonly object Address = new
    {
        street = "1 Main St",
        city = "Springfield",
        state = "IL",
        zipCode = "62701",
        country = "US"
    };

    private BasketApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartHost() => _factory = new BasketApiFactory();

    [OneTimeTearDown]
    public void StopHost() => _factory.Dispose();

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private async Task<(HttpClient Client, string UserId)> BasketWithAMugAsync()
    {
        var userId = NewUser();
        var mug = _factory.Catalog.Add("Mug", 10m);
        var client = _factory.CreateClientFor(userId);
        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId = mug, quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return (client, userId);
    }

    [Test]
    public async Task ACheckoutWithoutAPaymentMethod_Succeeds()
    {
        var (client, userId) = await BasketWithAMugAsync();
        using var _ = client;

        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/checkout", new { shippingAddress = Address });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checkoutId").GetGuid().Should().NotBeEmpty();
    }

    [Test]
    public async Task APaymentMethodStillSent_IsIgnored()
    {
        var (client, userId) = await BasketWithAMugAsync();
        using var _ = client;

        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/checkout",
            new { shippingAddress = Address, paymentMethod = "card" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an older client must keep working");
    }

    [Test]
    public async Task ANameAndPriceSentWithAnAdd_AreIgnored()
    {
        var userId = NewUser();
        var mug = _factory.Catalog.Add("Mug", 10m);
        using var client = _factory.CreateClientFor(userId);

        var response = await client.PostAsJsonAsync($"/api/v1/basket/{userId}/items",
            new { productId = mug, quantity = 1, productName = "Free mug", price = 0.01m });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var item = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/basket/{userId}")).GetProperty("items")[0];
        item.GetProperty("price").GetDecimal().Should().Be(10m);
        item.GetProperty("productName").GetString().Should().Be("Mug");
    }

    [Test]
    public async Task TheOpenApiDocument_DescribesTheCheckoutResponse_AndNoClientPricingOrPaymentMethod()
    {
        using var client = _factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var checkoutResponse = Resolve(document, document
            .GetProperty("paths").GetProperty("/api/v1/basket/{userId}/checkout").GetProperty("post")
            .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema"));
        PropertyNames(checkoutResponse).Should().Contain("checkoutId", "the response used to be declared as object");

        var addRequest = RequestSchema(document, "/api/v1/basket/{userId}/items");
        PropertyNames(addRequest).Should().Contain(["productId", "quantity"]).And.NotContain(["productName", "price"]);

        var checkoutRequest = RequestSchema(document, "/api/v1/basket/{userId}/checkout");
        PropertyNames(checkoutRequest).Should().Contain("shippingAddress").And.NotContain("paymentMethod");
    }

    private static JsonElement RequestSchema(JsonElement document, string path)
        => Resolve(document, document.GetProperty("paths").GetProperty(path).GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));

    /// <summary>Follows a <c>$ref</c> into <c>components/schemas</c>, or returns an inline schema as it is.</summary>
    private static JsonElement Resolve(JsonElement document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var name = reference.GetString()!.Split('/').Last();
        return document.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static IReadOnlyList<string> PropertyNames(JsonElement schema)
        => schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
}
