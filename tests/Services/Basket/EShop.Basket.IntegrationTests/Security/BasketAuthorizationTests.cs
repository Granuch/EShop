using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using EShop.Basket.IntegrationTests.Fixtures;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Basket.IntegrationTests.Security;

[TestFixture]
public class BasketAuthorizationTests
{
    [Test]
    public async Task GetBasket_WhenTokenIsMissing_ShouldReturnUnauthorized()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/basket/user-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetBasket_WhenTokenBelongsToOtherUser_ShouldReturnForbidden()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken("other-user", isAdmin: false));

        var response = await client.GetAsync("/api/v1/basket/user-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetBasket_WhenTokenBelongsToSameUser_ShouldBeAuthorized()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken("user-1", isAdmin: false));

        var response = await client.GetAsync("/api/v1/basket/user-1");

        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetBasket_WhenTokenBelongsToAdmin_ShouldBeAuthorized()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken("admin-user", isAdmin: true));

        var response = await client.GetAsync("/api/v1/basket/user-1");

        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
    }

    private static string CreateToken(string userId, bool isAdmin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var secret = "TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "EShop.Basket.Test",
            audience: "EShop.Test",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
