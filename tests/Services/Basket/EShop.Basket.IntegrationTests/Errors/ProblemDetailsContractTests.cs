using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EShop.Basket.IntegrationTests.Fixtures;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Basket.IntegrationTests.Errors;

/// <summary>
/// Guards the canonical RFC 7807 error envelope. Every EShop service must answer failures with
/// the same shape, from both the Result-failure path and the exception path.
/// </summary>
[TestFixture]
public class ProblemDetailsContractTests
{
    [Test]
    public async Task ResultFailure_ShouldReturnCanonicalProblemDetails()
    {
        await using var factory = new BasketApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken("user-1"));

        // Quantity 0 fails AddItemToBasketCommandValidator, which ValidationBehavior turns into
        // a Result failure (not an exception), so this exercises the ProblemResults path.
        var response = await client.PostAsJsonAsync(
            "/api/v1/basket/user-1/items",
            new { ProductId = Guid.NewGuid(), Quantity = 0 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/problem+json"));

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = problem.RootElement;

        // type and title are filled by the framework's ProblemDetailsDefaults for the status.
        // We never write them ourselves, precisely so they cannot drift from the values the
        // minimal-API Results.Problem path produces.
        Assert.That(root.GetProperty("type").GetString(), Does.StartWith("https://"),
            "type must be a URI, not a bare token");
        Assert.That(root.GetProperty("title").GetString(), Is.Not.Empty);
        Assert.That(root.GetProperty("status").GetInt32(), Is.EqualTo(400));
        Assert.That(root.GetProperty("detail").GetString(), Is.Not.Empty);
        Assert.That(root.GetProperty("errorCode").GetString(), Is.Not.Empty,
            "the machine-readable code lives in errorCode, not in title");
        Assert.That(root.GetProperty("traceId").GetString(), Is.Not.Empty);
    }

    private static string CreateToken(string userId)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!"));

        var token = new JwtSecurityToken(
            issuer: "EShop.Basket.Test",
            audience: "EShop.Test",
            claims: [new Claim(ClaimTypes.NameIdentifier, userId)],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
