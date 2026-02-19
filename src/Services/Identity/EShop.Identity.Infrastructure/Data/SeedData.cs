using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace EShop.Identity.Infrastructure.Data;

/// <summary>
/// Seeds default roles and admin user
/// </summary>
public static class SeedData
{
    public static async Task SeedRolesAsync(
        RoleManager<ApplicationRole> roleManager,
        ILogger? logger = null)
    {
        var roles = new[] { "Admin", "User" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var applicationRole = new ApplicationRole
                {
                    Name = role,
                    Description = $"{role} role for the application",
                    CreatedAt = DateTime.UtcNow
                };

                var result = await roleManager.CreateAsync(applicationRole);
                if (result.Succeeded)
                {
                    logger?.Information("Created role: {Role}", role);
                }
            }
        }
    }

    public static async Task SeedRolesAndAdminAsync(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger? logger = null)
    {
        await SeedRolesAsync(roleManager, logger);

        var adminEmail = configuration["Identity:SeedAdminEmail"] ?? "admin@eshop.com";
        var adminPassword = configuration["Identity:SeedAdminPassword"];

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger?.Warning(
                "Identity:SeedAdminPassword is not configured. Skipping admin user seed. " +
                "Set this value in environment variables or user-secrets.");
            return;
        }

        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                logger?.Information("Created admin user with configured email");
            }
            else
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                logger?.Error("Failed to create admin user: {Errors}", errors);
            }
        }
    }
}
