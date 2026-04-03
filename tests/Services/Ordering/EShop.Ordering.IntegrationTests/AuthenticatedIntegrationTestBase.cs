using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Ordering.IntegrationTests;

/// <summary>
/// Base class for integration tests that require authentication.
/// Generates a test JWT token and sets the bearer header before each test.
/// </summary>
[Category("Integration")]
public abstract class AuthenticatedIntegrationTestBase : IntegrationTestBase
{
    protected string AccessToken { get; private set; } = string.Empty;

    protected virtual string TestUserRole => "Admin";
    protected virtual string TestUserId => "test-user-id-1";
    protected virtual string TestUserEmail => "admin@test.com";

    [SetUp]
    public override async Task SetUpAsync()
    {
        await base.SetUpAsync();

        AccessToken = GenerateTestJwtToken();
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken);
    }

    [TearDown]
    public override async Task TearDownAsync()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        await base.TearDownAsync();
    }

    private string GenerateTestJwtToken()
    {
        using var scope = Factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var jwtKey = configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            jwtKey = Fixtures.OrderingApiFactory.TestJwtSecretKey;
        }

        var issuer = configuration["JwtSettings:Issuer"];
        if (string.IsNullOrWhiteSpace(issuer))
        {
            issuer = Fixtures.OrderingApiFactory.TestJwtIssuer;
        }

        var audience = configuration["JwtSettings:Audience"];
        if (string.IsNullOrWhiteSpace(audience))
        {
            audience = Fixtures.OrderingApiFactory.TestJwtAudience;
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
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
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
