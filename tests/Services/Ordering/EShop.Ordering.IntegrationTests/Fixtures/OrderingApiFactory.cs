using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Data;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace EShop.Ordering.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Ordering integration tests. On its own it uses an InMemory
/// database; <see cref="PostgresOrderingApiFactory"/>, the default for <see cref="IntegrationTestBase"/>,
/// swaps in a real PostgreSQL database.
/// </summary>
public class OrderingApiFactory : WebApplicationFactory<Program>
{
    internal const string TestJwtSecretKey = "THIS_IS_A_TEST_ONLY_SECRET_KEY_32_CHARS_MINIMUM";
    internal const string TestJwtIssuer = "ESHOP_ORDERING_TEST_ISSUER";
    internal const string TestJwtAudience = "ESHOP_ORDERING_TEST_AUDIENCE";

    private readonly string _databaseName;
    private bool _databaseSeeded;

    /// <summary>The products this host can price orders from. See <see cref="FakeProductCatalog"/>.</summary>
    public FakeProductCatalog Catalog { get; } = new();

    public OrderingApiFactory()
    {
        _databaseName = $"OrderingTestDb_{Guid.NewGuid()}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting, not ConfigureAppConfiguration. Program.cs reads JwtSettings in its
        // top-level statements while composing the app and throws if SecretKey is blank;
        // ConfigureAppConfiguration sources are only applied when the host is finally built,
        // which is after that read. This appeared to work locally only because a developer shell
        // exported JwtSettings__SecretKey — on a clean checkout (and in CI) the guard fired and
        // every test in this assembly failed at SetUp.
        builder.UseSetting("JwtSettings:SecretKey", TestJwtSecretKey);
        builder.UseSetting("JwtSettings:Issuer", TestJwtIssuer);
        builder.UseSetting("JwtSettings:Audience", TestJwtAudience);

        // Satisfies CatalogServiceOptions' startup validation; the reader itself is replaced below.
        builder.UseSetting("CatalogService:BaseUrl", "http://catalog.test/");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext and IUnitOfWork registrations
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OrderingDbContext>) ||
                            d.ServiceType == typeof(OrderingDbContext) ||
                            d.ServiceType == typeof(DbContext) ||
                            d.ServiceType == typeof(IUnitOfWork))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            ConfigureDatabase(services);

            // Re-register IUnitOfWork with the new DbContext
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<OrderingDbContext>());

            // Re-register DbContext base type for OutboxProcessorService
            services.AddScoped<DbContext>(provider => provider.GetRequiredService<OrderingDbContext>());

            // Catalog is an HTTP dependency; tests price from an in-process double instead.
            services.RemoveAll<IProductCatalogReader>();
            services.AddSingleton<IProductCatalogReader>(Catalog);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = TestJwtIssuer,
                    ValidAudience = TestJwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecretKey)),
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };
            });

            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// Scope validation on, as <c>WebApplicationBuilder</c> does only in Development. The test host
    /// runs as "Testing", where it is off by default, so a Singleton holding a scoped service was
    /// resolved from the root provider here exactly as in Sandbox and Production — and every test
    /// stayed green while <c>OrderOwnerOrAdminHandler</c> shared one <c>OrderingDbContext</c> across
    /// all requests (Ordering audit H1). With this, any captive dependency fails host build, and so
    /// fails every test in the assembly.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        return base.CreateHost(builder);
    }

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// InMemory, one database per factory. <see cref="PostgresOrderingApiFactory"/> — the default for
    /// <see cref="IntegrationTestBase"/> since Ordering audit M11 — replaces this with Npgsql.
    /// </summary>
    protected virtual void ConfigureDatabase(IServiceCollection services)
    {
        services.AddDbContext<OrderingDbContext>(options => options.UseInMemoryDatabase(_databaseName));
    }

    /// <summary>InMemory has no migrations, so it needs <c>EnsureCreated</c>; the Postgres factory overrides this.</summary>
    protected virtual Task EnsureSchemaAsync(OrderingDbContext db) => db.Database.EnsureCreatedAsync();

    public async Task InitializeDatabaseAsync()
    {
        if (_databaseSeeded) return;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OrderingApiFactory>>();

        await EnsureSchemaAsync(db);

        try
        {
            await SeedTestDataAsync(db);
            _databaseSeeded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred seeding the ordering test database. Error: {Message}", ex.Message);
        }
    }

    private static async Task SeedTestDataAsync(OrderingDbContext db)
    {
        if (db.Orders.Any()) return;

        // Seed a sample order for read tests
        var address = new EShop.Ordering.Domain.ValueObjects.Address(
            "123 Test St", "TestCity", "TS", "12345", "US");

        var items = new List<EShop.Ordering.Domain.Entities.OrderItem>
        {
            new(Guid.NewGuid(), "Seeded Product A", 29.99m, 2),
            new(Guid.NewGuid(), "Seeded Product B", 49.99m, 1)
        };

        var order = EShop.Ordering.Domain.Entities.Order.Create("seed-user-1", address, items);
        order.ClearDomainEvents(); // Avoid outbox issues in testing

        await db.Orders.AddAsync(order);
        await db.SaveChangesAsync();
    }
}
