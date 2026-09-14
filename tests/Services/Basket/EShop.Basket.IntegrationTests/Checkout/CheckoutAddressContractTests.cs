using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using EShop.Basket.IntegrationTests.Fixtures;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Basket.IntegrationTests.Checkout;

/// <summary>
/// Ordering audit decision D4 (docs/audits/ordering/DECISIONS.md): checkout takes a structured
/// <c>shippingAddress</c> object only. The legacy free-text string must be refused at binding — before
/// anything is validated, locked or cleared — because the only way to honour it is to comma-parse it,
/// and that parsing is what made checkouts dead-letter after the basket was already gone (audit C2).
///
/// <para>
/// Both requests below are 400s (the fixture's Redis holds no basket), so status alone cannot tell
/// them apart. What does is <c>errorCode</c>: every answer from the checkout pipeline carries one
/// (<c>Validation.Failed</c> or a <c>Basket.*</c> code), and a request that failed to bind never
/// reaches that pipeline. The structured control proves the discriminator works; without it the string
/// test could pass for the wrong reason.
/// </para>
/// </summary>
[TestFixture]
public class CheckoutAddressContractTests
{
    private const string CheckoutUrl = "/api/v1/basket/user-1/checkout";

    [Test]
    public async Task ALegacyStringAddress_IsRefusedAtBinding_AndNeverReachesCheckout()
    {
        await using var factory = new BasketApiFactory();
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(CheckoutUrl, new
        {
            shippingAddress = "1 Main St, Springfield, IL, 62701, US",
            paymentMethod = "card"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), body);
        Assert.That(body, Does.Not.Contain("errorCode"),
            "an errorCode means the string bound and reached the checkout pipeline — i.e. dual support is back");
    }

    [Test]
    public async Task AStructuredAddress_ReachesCheckout()
    {
        await using var factory = new BasketApiFactory();
        var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(CheckoutUrl, new
        {
            shippingAddress = new
            {
                street = "1 Main St",
                city = "Springfield",
                state = "IL",
                zipCode = "62701",
                country = "US"
            },
            paymentMethod = "card"
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("errorCode"),
            "the control must reach the checkout pipeline, or the string test proves nothing");
    }

    private static HttpClient AuthenticatedClient(BasketApiFactory factory)
    {
        var client = factory.CreateClient();
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!"));
        var token = new JwtSecurityToken(
            issuer: "EShop.Basket.Test",
            audience: "EShop.Test",
            claims: [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
}
