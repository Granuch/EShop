using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace EShop.ApiGateway.IntegrationTests.Fixtures;

/// <summary>
/// A gateway host for exercising route authorization, plus the tokens to drive it.
///
/// <para>
/// It differs from <see cref="GatewayApiFactory"/> in one way: it raises the global rate limit,
/// which the base fixture pins at <b>one request per minute</b> for its own throttling assertions.
/// These tests send dozens of requests through one host, so without this every assertion after the
/// first reads <c>429</c> — which looks exactly like an authorization failure and is why the first
/// run of this fixture reported six "authorization" failures that were nothing of the kind.
/// </para>
///
/// <para>
/// <b>It must be raised through <c>ConfigureAppConfiguration</c>, not <c>UseSetting</c></b>, which
/// is the opposite of the usual rule in this repo. The root guide's warning is about values
/// <c>Program.cs</c> reads while composing the app, where host configuration arrives in time and an
/// app-configuration source does not. Here both arrive in time, and the ordinary precedence then
/// applies: the base fixture's <c>ConfigureAppConfiguration</c> source is added after host
/// configuration and so <i>overrides</i> a <c>UseSetting</c> value. Verified by observation — the
/// <c>UseSetting</c> form left the limit at the base fixture's 1 and every request after the first
/// came back 429.
/// </para>
/// </summary>
public sealed class RouteAuthorizationApiFactory : GatewayApiFactory
{
    public const string SecretKey = "TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!";
    public const string Issuer = "EShop.Identity";
    public const string Audience = "EShop.Services";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Registered after the base fixture's source, so this wins.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:GlobalPermitLimit"] = "100000",
                ["RateLimiting:GlobalWindowSeconds"] = "60"
            });
        });
    }

    /// <summary>A token for a signed-in customer holding no roles.</summary>
    public static string UserToken() => Token("user-1", roles: []);

    /// <summary>A token for an administrator.</summary>
    public static string AdminToken() => Token("admin-1", roles: ["Admin"]);

    /// <summary>
    /// A token carrying one <c>permission</c> claim and no role — how a test shows that a permission policy admits a
    /// caller who is not an Admin, and refuses one holding a different permission.
    /// </summary>
    public static string PermissionToken(string permission)
        => Token("operator-1", roles: [], new Claim(EShopPermissions.ClaimType, permission));

    private static string Token(string subject, string[] roles, params Claim[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(extraClaims);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
