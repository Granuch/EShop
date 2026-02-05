using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Helpers;

/// <summary>
/// Helper methods for managing test users
/// </summary>
public static class UserManagementHelper
{
    public static async Task<string> CreateTestUserAsync(
        IServiceProvider services,
        string? email = null,
        string? password = null,
        string role = TestUsers.Roles.User,
        bool emailConfirmed = true,
        bool isActive = true)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        
        var testEmail = email ?? $"test_{Guid.NewGuid()}@test.com";
        var testPassword = password ?? "Test@123456";

        var user = new ApplicationUser
        {
            UserName = testEmail,
            Email = testEmail,
            FirstName = "Test",
            LastName = "User",
            EmailConfirmed = emailConfirmed,
            IsActive = isActive
        };

        var result = await userManager.CreateAsync(user, testPassword);
        if (!result.Succeeded)
        {
            throw new Exception($"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);
        
        return user.Id;
    }

    public static async Task DeleteTestUserAsync(IServiceProvider services, string userId)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        
        if (user != null)
        {
            await userManager.DeleteAsync(user);
        }
    }

    public static async Task<string?> GetUserIdByEmailAsync(IServiceProvider services, string email)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user?.Id;
    }

    public static async Task ChangeUserPasswordAsync(
        IServiceProvider services,
        string userId,
        string newPassword)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        
        if (user == null)
        {
            throw new Exception($"User with ID {userId} not found");
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        
        if (!result.Succeeded)
        {
            throw new Exception($"Failed to change password: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }
}
