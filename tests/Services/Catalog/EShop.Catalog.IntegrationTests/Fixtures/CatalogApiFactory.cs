using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Catalog Integration tests.
///
/// <para>
/// This base uses the EF InMemory provider. Prefer
/// <see cref="PostgresCatalogApiFactory"/> — <see cref="IntegrationTestBase"/> uses it by default —
/// because InMemory cannot execute the provider-specific paths this service actually ships
/// (<c>EF.Functions.ILike</c>, unique/GIN indexes, the partial unique index on
/// <c>ProductImages</c>, decimal precision, column length caps). This class remains as the shared
/// host/JWT/seed configuration both providers build on, and for the rare fixture that genuinely
/// does not touch the database.
/// </para>
/// </summary>
public class CatalogApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName;
    private bool _databaseSeeded;
    private readonly bool _useSharedDatabase;

    public CatalogApiFactory(bool useSharedDatabase = false)
    {
        _useSharedDatabase = useSharedDatabase;
        _databaseName = useSharedDatabase
            ? "SharedCatalogTestDb"
            : $"CatalogTestDb_{Guid.NewGuid()}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Catalog's tracked appsettings.json ships "SecretKey": "" and there is no
        // appsettings.Testing.json, so nothing supplies a JWT key under the Testing environment
        // and Program.cs throws while composing the app. The suite passed locally only because a
        // developer shell exported JwtSettings__SecretKey; on a clean checkout (and in CI) all 91
        // tests failed at SetUp. UseSetting rather than ConfigureAppConfiguration because the
        // value is read in top-level statements, before ConfigureAppConfiguration sources apply.
        builder.UseSetting("JwtSettings:SecretKey", "TestSecretKeyThatIsLongEnoughForHS256Algorithm12345!");
        builder.UseSetting("JwtSettings:Issuer", "EShop.Identity");
        builder.UseSetting("JwtSettings:Audience", "EShop.Services");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext and IUnitOfWork registrations
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>) ||
                            d.ServiceType == typeof(CatalogDbContext) ||
                            d.ServiceType == typeof(DbContext) ||
                            d.ServiceType == typeof(IUnitOfWork))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            ConfigureDatabase(services);

            // Re-register IUnitOfWork with the new DbContext
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CatalogDbContext>());

            // Re-register DbContext base type for OutboxProcessorService
            services.AddScoped<DbContext>(provider => provider.GetRequiredService<CatalogDbContext>());

            // Allow derived classes to configure additional services
            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// Registers the DbContext. Overridden by <see cref="PostgresCatalogApiFactory"/> to swap the
    /// provider; both this and the override register exactly one, so the container never ends up
    /// with two (which throws <i>"Only a single database provider can be registered in a service
    /// provider"</i>).
    /// </summary>
    protected virtual void ConfigureDatabase(IServiceCollection services)
    {
        // InMemory database with a fixed name per factory instance.
        services.AddDbContext<CatalogDbContext>(options =>
        {
            options.UseInMemoryDatabase(_databaseName);
        });
    }

    /// <summary>
    /// Override this method in derived factories to add test-specific service configurations.
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Default implementation does nothing
    }

    /// <summary>
    /// Creates the schema. InMemory has no migrations, so it needs <c>EnsureCreated</c>; the
    /// relational factory overrides this to a no-op because its database is cloned from an
    /// already-migrated template and <c>Program.cs</c> runs <c>MigrateAsync</c> at startup anyway.
    /// </summary>
    protected virtual async Task EnsureSchemaAsync(CatalogDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_databaseSeeded) return;

        using var scope = Services.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var db = scopedServices.GetRequiredService<CatalogDbContext>();
        var logger = scopedServices.GetRequiredService<ILogger<CatalogApiFactory>>();

        await EnsureSchemaAsync(db);

        try
        {
            await SeedTestDataAsync(db);
            _databaseSeeded = true;
        }
        catch (Exception ex)
        {
            // M16. This used to log and return, leaving _databaseSeeded false and the tests to run
            // against an unseeded database — so a broken seed surfaced later as a pile of
            // unrelated-looking assertion failures instead of at setup. Log for the detail, then
            // rethrow so the failure is attributed where it happened.
            logger.LogError(ex, "An error occurred seeding the database with test data. Error: {Message}", ex.Message);
            throw;
        }
    }

    private static async Task SeedTestDataAsync(CatalogDbContext db)
    {
        // Seed default categories
        if (!db.Categories.Any())
        {
            var electronics = EShop.Catalog.Domain.Entities.Category.Create(
                "Electronics", "electronics", null);

            var clothing = EShop.Catalog.Domain.Entities.Category.Create(
                "Clothing", "clothing", null);

            var books = EShop.Catalog.Domain.Entities.Category.Create(
                "Books", "books", null);

            await db.Categories.AddRangeAsync(electronics, clothing, books);
            await db.SaveChangesAsync();

            // Seed default products
            var product1 = EShop.Catalog.Domain.Entities.Product.Create(
                "Laptop Pro 15", "ELEC-LP-001", 1299.99m, 50, electronics.Id);

            var product2 = EShop.Catalog.Domain.Entities.Product.Create(
                "Wireless Mouse", "ELEC-WM-002", 29.99m, 200, electronics.Id);

            var product3 = EShop.Catalog.Domain.Entities.Product.Create(
                "T-Shirt Basic", "CLTH-TS-001", 19.99m, 500, clothing.Id);

            // D1 / H5a. Product.Create yields Draft, and the public read paths now return published
            // products only — an unpublished seed would make the seeded catalog invisible to every
            // anonymous test, which is exactly the bug this stage fixed rather than a test to keep.
            product1.Publish();
            product2.Publish();
            product3.Publish();

            await db.Products.AddRangeAsync(product1, product2, product3);
            await db.SaveChangesAsync();
        }
    }
}
