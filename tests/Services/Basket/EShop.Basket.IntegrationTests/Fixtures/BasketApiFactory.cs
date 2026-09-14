using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Infrastructure.Outbox;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// The Basket host on a real Redis database of its own (Basket audit S2) — the default, not an opt-in.
/// Catalog is replaced by <see cref="FakeProductCatalog"/>; nothing else is mocked.
///
/// <para>
/// Every setting goes through <c>UseSetting</c> (host configuration). <c>Program.cs</c> reads the JWT settings and
/// the Redis connection string while composing the app, and a <c>ConfigureAppConfiguration</c> source arrives too
/// late; until S2 the JWT values only worked because <c>appsettings.Testing.json</c> held the same ones.
/// </para>
/// </summary>
public class BasketApiFactory : WebApplicationFactory<Program>
{
    public const string JwtSecret = "TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!";
    private const string JwtIssuer = "EShop.Basket.Test";
    private const string JwtAudience = "EShop.Test";

    /// <summary>Allocates the Redis database up front: it must exist before the host is built.</summary>
    public BasketApiFactory()
    {
        RedisConnectionString = RedisTestServer.CreateDatabaseAsync().GetAwaiter().GetResult();
    }

    /// <summary>The connection string of this factory's own Redis database.</summary>
    public string RedisConnectionString { get; }

    /// <summary>The Catalog this host prices from.</summary>
    public FakeProductCatalog Catalog { get; } = new();

    /// <summary>The host's own multiplexer, for asserting on what it stored.</summary>
    public IConnectionMultiplexer Redis => Services.GetRequiredService<IConnectionMultiplexer>();

    /// <summary>A client carrying a token for <paramref name="userId"/>.</summary>
    public HttpClient CreateClientFor(string userId, bool isAdmin = false)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId, isAdmin));
        return client;
    }

    public static string CreateToken(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("JwtSettings:SecretKey", JwtSecret);
        builder.UseSetting("JwtSettings:Issuer", JwtIssuer);
        builder.UseSetting("JwtSettings:Audience", JwtAudience);
        builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
            });

            // Only registered when RabbitMQ is configured, which Testing never is; removed anyway, so a test can
            // never race a background drain of the outbox it is asserting on.
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType == typeof(BasketRedisOutboxProcessorService))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IProductCatalogReader>(Catalog);
        });
    }
}
