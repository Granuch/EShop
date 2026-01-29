using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Serilog;

namespace EShop.Identity.Infrastructure.Data;

/// <summary>
/// Seeds default roles and admin user
/// </summary>
public static class SeedData
{
    public static async Task SeedRolesAndAdminAsync(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger? logger = null)
    {
        // Create default roles
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

        // Create admin user if not exists
        var adminEmail = "admin@eshop.com";
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

            var result = await userManager.CreateAsync(adminUser, "Admin@123456");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                logger?.Information("Created admin user: {Email}", adminEmail);
            }
            else
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                logger?.Error("Failed to create admin user: {Errors}", errors);
            }
        }
    }
}
