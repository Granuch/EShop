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

    private string GenerateTestJwtToken() => CreateToken(TestUserRole);

    /// <summary>
    /// A token for <see cref="TestUserId"/> carrying <paramref name="role"/> (none when <c>null</c>) and any extra claims —
    /// a <c>permission</c> claim with no role is how a test shows a permission policy admits a non-Admin caller.
    /// </summary>
    protected string CreateToken(string? role, params Claim[] extraClaims)
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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, TestUserId),
            new(ClaimTypes.Email, TestUserEmail),
            new(JwtRegisteredClaimNames.Sub, TestUserId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
