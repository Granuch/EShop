using EShop.BuildingBlocks.Domain;
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
                            d.ServiceType == typeof(IUnitOfWork))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Add InMemory database with fixed name per factory instance
            services.AddDbContext<IdentityDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Re-register IUnitOfWork with the new DbContext
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<IdentityDbContext>());
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        if (_databaseSeeded) return;

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
                EmailConfirmed = true,
                IsActive = true
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
                EmailConfirmed = true,
                IsActive = true
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
                EmailConfirmed = true,
                IsActive = false
            };

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
                EmailConfirmed = false,
                IsActive = true
            };

            await userManager.CreateAsync(unconfirmedUser, "Unconfirmed@123456");
            await userManager.AddToRoleAsync(unconfirmedUser, "User");
        }
    }
}
