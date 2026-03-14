using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Ordering Integration tests.
/// Uses In-Memory database for testing.
/// </summary>
public class OrderingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName;
    private bool _databaseSeeded;

    public OrderingApiFactory()
    {
        _databaseName = $"OrderingTestDb_{Guid.NewGuid()}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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

            // Add InMemory database with unique name per factory instance
            services.AddDbContext<OrderingDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Re-register IUnitOfWork with the new DbContext
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<OrderingDbContext>());

            // Re-register DbContext base type for OutboxProcessorService
            services.AddScoped<DbContext>(provider => provider.GetRequiredService<OrderingDbContext>());

            ConfigureTestServices(services);
        });
    }

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_databaseSeeded) return;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OrderingApiFactory>>();

        await db.Database.EnsureCreatedAsync();

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
