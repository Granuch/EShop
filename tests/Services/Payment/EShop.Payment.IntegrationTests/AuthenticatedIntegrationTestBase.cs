using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Payment.IntegrationTests;

[Category("Integration")]
public abstract class AuthenticatedIntegrationTestBase : IntegrationTestBase
{
    protected string AccessToken { get; private set; } = string.Empty;

    protected virtual string TestUserRole => "User";
    protected virtual string TestUserId => "test-user-id-1";
    protected virtual string TestUserEmail => "user@test.com";

    [SetUp]
    public override Task SetUpAsync()
    {
        base.SetUpAsync();

        AccessToken = GenerateTestJwtToken();
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);

        return Task.CompletedTask;
    }

    [TearDown]
    public override Task TearDownAsync()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        return base.TearDownAsync();
    }

    private string GenerateTestJwtToken()
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PaymentApiFactory.TestJwtSecretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestUserId),
            new Claim(ClaimTypes.Email, TestUserEmail),
            new Claim(ClaimTypes.Role, TestUserRole),
            new Claim(JwtRegisteredClaimNames.Sub, TestUserId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: PaymentApiFactory.TestJwtIssuer,
            audience: PaymentApiFactory.TestJwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
