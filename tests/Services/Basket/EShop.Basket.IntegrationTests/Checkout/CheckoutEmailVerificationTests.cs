using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using FluentAssertions;

namespace EShop.Basket.IntegrationTests.Checkout;

/// <summary>
/// Soft email verification, on real Redis: a customer who has not confirmed their address may fill and read a basket,
/// but checking it out places an order, so it needs the token's <c>email_verified=true</c>. The refusal is a 403
/// <c>Auth.EmailNotConfirmed</c> in the shared envelope, and it happens before the basket is read — the basket is still
/// there afterwards and nothing is queued for Ordering, so confirming and refreshing lets the same basket check out.
/// This is the storefront's order entry point; Ordering receives the order only as an event, with no token to check.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CheckoutEmailVerificationTests
{
    private const string PendingOutbox = "basket:outbox:pending";

    private static readonly object CheckoutBody = new
    {
        shippingAddress = new
        {
            street = "1 Main St",
            city = "Springfield",
            state = "IL",
            zipCode = "62701",
            country = "US"
        }
    };

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, string userId, Guid productId)
        => client.PostAsJsonAsync($"/api/v1/basket/{userId}/items", new { productId, quantity = 1 });

    private static Task<HttpResponseMessage> CheckOutAsync(HttpClient client, string userId)
        => client.PostAsJsonAsync($"/api/v1/basket/{userId}/checkout", CheckoutBody);

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [TestCase("false", TestName = "An unverified customer cannot check out")]
    [TestCase(null, TestName = "A token without the claim cannot check out")]
    public async Task AnUnverifiedCaller_Gets403_AndTheBasketIsUntouched(string? emailVerified)
    {
        await using var factory = new BasketApiFactory();
        var productId = factory.Catalog.Add("Mug", 12.50m);
        var userId = NewUser();
        using var client = factory.CreateClientFor(userId, emailVerified: emailVerified);

        (await AddAsync(client, userId, productId)).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "filling a basket needs no confirmed address");

        var response = await CheckOutAsync(client, userId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await BodyOf(response);
        body.GetProperty("errorCode").GetString().Should().Be("Auth.EmailNotConfirmed");
        body.GetProperty("detail").GetString().Should().Be(EmailVerification.NotConfirmed.Message);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

        (await factory.Redis.GetDatabase().ListLengthAsync(PendingOutbox)).Should().Be(0,
            "nothing may reach Ordering: the event is the order");
        var basket = await BodyOf(await client.GetAsync($"/api/v1/basket/{userId}"));
        basket.GetProperty("items").GetArrayLength().Should().Be(1, "the refusal comes before the basket is touched");
    }

    [Test]
    public async Task OnceVerified_TheSameBasketChecksOut()
    {
        await using var factory = new BasketApiFactory();
        var productId = factory.Catalog.Add("Mug", 12.50m);
        var userId = NewUser();
        using var unverified = factory.CreateClientFor(userId, emailVerified: "false");
        (await AddAsync(unverified, userId, productId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CheckOutAsync(unverified, userId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The customer confirms and refreshes: the new token says true.
        using var verified = factory.CreateClientFor(userId, emailVerified: "true");
        var response = await CheckOutAsync(verified, userId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await factory.Redis.GetDatabase().ListLengthAsync(PendingOutbox)).Should().Be(1);
    }
}
