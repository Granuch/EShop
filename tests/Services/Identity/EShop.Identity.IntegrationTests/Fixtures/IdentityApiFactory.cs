using EShop.BuildingBlocks.Domain;
using EShop.Identity.Application.Telemetry;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using EShop.Identity.Infrastructure.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Integration tests
/// Uses In-Memory database for testing
/// </summary>
public class IdentityApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName;
    private bool _databaseSeeded = false;

    public IdentityApiFactory()
    {
        _databaseName = $"IdentityTestDb_{Guid.NewGuid()}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext and IUnitOfWork registrations
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<IdentityDbContext>) ||
                            d.ServiceType == typeof(IdentityDbContext) ||
                            d.ServiceType == typeof(DbContext) ||
                            d.ServiceType == typeof(IUnitOfWork))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // The provider is registered exactly once, by this one call. It must NOT be
            // "register InMemory here, then remove and re-add Npgsql in a subclass": EF registers
            // provider services beyond DbContextOptions<T>, so a second AddDbContext with a
            // different provider fails with "Only a single database provider can be registered in
            // a service provider" no matter which descriptors you strip first. Overriding the
            // registration is the supported shape — see PostgresIdentityApiFactory.
            ConfigureDatabase(services);

            // Re-register IUnitOfWork with the new DbContext
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<IdentityDbContext>());

            // Re-register DbContext base type for OutboxProcessorService
            services.AddScoped<DbContext>(provider => provider.GetRequiredService<IdentityDbContext>());

            // Allow derived classes to configure additional services
            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// Registers the EF provider. Override to run a fixture against something other than
    /// InMemory; do not add a second provider alongside this one.
    /// </summary>
    protected virtual void ConfigureDatabase(IServiceCollection services)
    {
        services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseInMemoryDatabase(_databaseName);
        });
    }

    /// <summary>
    /// Override this method in derived factories to add test-specific service configurations
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Default implementation does nothing
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_databaseSeeded) return;

        // Ensure telemetry metrics are initialized for test context
        var metrics = Services.GetService<IIdentityMetrics>();
        if (metrics != null)
        {
            IdentityTelemetry.Initialize(metrics);
        }

        using var scope = Services.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var db = scopedServices.GetRequiredService<IdentityDbContext>();
        var logger = scopedServices.GetRequiredService<ILogger<IdentityApiFactory>>();

        await db.Database.EnsureCreatedAsync();

        try
        {
            await SeedTestDataAsync(scopedServices);
            _databaseSeeded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred seeding the database with test data. Error: {Message}", ex.Message);
        }
    }

    private static async Task SeedTestDataAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Create roles
        var roles = new[] { "Admin", "User" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = role,
                    Description = $"{role} role for testing"
                });
            }
        }

        // Create test admin user
        var adminEmail = "admin@test.com";
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "Test",
                EmailConfirmed = true
            };

            await userManager.CreateAsync(adminUser, "Admin@123456");
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }

        // Create test regular user
        var userEmail = "user@test.com";
        if (await userManager.FindByEmailAsync(userEmail) == null)
        {
            var regularUser = new ApplicationUser
            {
                UserName = userEmail,
                Email = userEmail,
                FirstName = "Regular",
                LastName = "User",
                EmailConfirmed = true
            };

            await userManager.CreateAsync(regularUser, "User@123456");
            await userManager.AddToRoleAsync(regularUser, "User");
        }

        // Create inactive user for testing
        var inactiveEmail = "inactive@test.com";
        if (await userManager.FindByEmailAsync(inactiveEmail) == null)
        {
            var inactiveUser = new ApplicationUser
            {
                UserName = inactiveEmail,
                Email = inactiveEmail,
                FirstName = "Inactive",
                LastName = "User",
                EmailConfirmed = true
            };
            inactiveUser.Deactivate();

            await userManager.CreateAsync(inactiveUser, "Inactive@123456");
            await userManager.AddToRoleAsync(inactiveUser, "User");
        }

        // Create unconfirmed user
        var unconfirmedEmail = "unconfirmed@test.com";
        if (await userManager.FindByEmailAsync(unconfirmedEmail) == null)
        {
            var unconfirmedUser = new ApplicationUser
            {
                UserName = unconfirmedEmail,
                Email = unconfirmedEmail,
                FirstName = "Unconfirmed",
                LastName = "User",
                EmailConfirmed = false
            };

            await userManager.CreateAsync(unconfirmedUser, "Unconfirmed@123456");
            await userManager.AddToRoleAsync(unconfirmedUser, "User");
        }
    }
}
