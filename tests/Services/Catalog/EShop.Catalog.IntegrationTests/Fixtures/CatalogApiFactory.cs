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
/// Uses In-Memory database for testing.
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

            // Add InMemory database with fixed name per factory instance
            services.AddDbContext<CatalogDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Re-register IUnitOfWork with the new DbContext
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CatalogDbContext>());

            // Re-register DbContext base type for OutboxProcessorService
            services.AddScoped<DbContext>(provider => provider.GetRequiredService<CatalogDbContext>());

            // Allow derived classes to configure additional services
            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// Override this method in derived factories to add test-specific service configurations.
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Default implementation does nothing
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_databaseSeeded) return;

        using var scope = Services.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var db = scopedServices.GetRequiredService<CatalogDbContext>();
        var logger = scopedServices.GetRequiredService<ILogger<CatalogApiFactory>>();

        await db.Database.EnsureCreatedAsync();

        try
        {
            await SeedTestDataAsync(db);
            _databaseSeeded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred seeding the database with test data. Error: {Message}", ex.Message);
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

            await db.Products.AddRangeAsync(product1, product2, product3);
            await db.SaveChangesAsync();
        }
    }
}
