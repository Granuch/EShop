using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Notification.IntegrationTests.Fixtures;

/// <summary>
/// The host as Testing (EF InMemory, no bus), given the settings <c>NotificationConfigurationGuard</c> requires in every
/// environment (Notification audit S4, plus the token settings Admin panel S12 added). They go through
/// <c>UseSetting</c>: <c>Program.cs</c> reads them while composing the host, where <c>ConfigureAppConfiguration</c>
/// would land too late.
/// </summary>
public class NotificationApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Long enough for HS256, and free of every pattern <c>JwtSecretGuard</c> refuses.</summary>
    public const string SecretKey = "NotificationSuiteSigningMaterialLongEnoughForHS256!";

    public const string Issuer = "EShop.Identity";
    public const string Audience = "EShop.Services";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Smtp:Host", "smtp.invalid");
        builder.UseSetting("IdentityService:BaseUrl", "http://identity.invalid/");
        builder.UseSetting("PasswordReset:ResetUrlBase", "https://shop.eshop-real.test/reset-password");
        builder.UseSetting("JwtSettings:SecretKey", SecretKey);
        builder.UseSetting("JwtSettings:Issuer", Issuer);
        builder.UseSetting("JwtSettings:Audience", Audience);
    }

    /// <summary>A client carrying an administrator's token, which the <c>Admin</c> bundle grants every permission.</summary>
    public HttpClient CreateAdminClient() => CreateClientWith(AdminToken());

    /// <summary>
    /// A client carrying a valid token for a signed-in customer holding no roles. Without one, a suite that only ever
    /// signs in as Admin cannot tell <c>notifications.read</c> from <c>RequireAuthorization()</c>.
    /// </summary>
    public HttpClient CreateUserClient() => CreateClientWith(UserToken());

    private HttpClient CreateClientWith(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static string UserToken() => Token("user-1", roles: []);

    public static string AdminToken() => Token("admin-1", roles: ["Admin"]);

    /// <summary>A token whose only claim to the journal is the permission itself — no role at all.</summary>
    public static string PermissionOnlyToken(string permission) => Token("operator-1", roles: [], permissions: [permission]);

    private static string Token(string subject, string[] roles, string[]? permissions = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange((permissions ?? []).Select(p => new Claim(EShopPermissions.ClaimType, p)));

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
