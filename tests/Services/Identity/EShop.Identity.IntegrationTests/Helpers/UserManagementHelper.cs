using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
            EmailConfirmed = emailConfirmed
        };

        if (!isActive)
        {
            user.Deactivate();
        }

        var result = await userManager.CreateAsync(user, testPassword);
        if (!result.Succeeded)
        {
            throw new Exception($"Failed to create test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);
        
        return user.Id;
    }

    /// <summary>
    /// Grants an additional role (Admin panel S6).
    /// </summary>
    /// <remarks>
    /// Exists so a test can build a user with <b>two</b> roles. Every seeded user has exactly one,
    /// which makes them useless for proving that the admin list's role filter does not multiply
    /// rows: a <c>Join</c> over <c>user_roles</c> returns one row per (user, role) pair, and with
    /// one role per user that is indistinguishable from the correct subquery.
    /// </remarks>
    public static async Task AddRoleAsync(IServiceProvider services, string userId, string role)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' was not found.");

        var result = await userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to add role '{role}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    /// <summary>
    /// Marks a user soft-deleted (Admin panel S6), the way <c>DeleteUserCommand</c> does.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="DeleteTestUserAsync"/>, which calls
    /// <c>UserManager.DeleteAsync</c> and removes the row outright — that cannot exercise anything
    /// about soft deletion, because the user is simply gone rather than hidden behind the
    /// <c>!IsDeleted</c> global query filter. Written through the DbContext rather than
    /// <c>UserManager.UpdateAsync</c> so the filter does not hide the row from the very update that
    /// is trying to set the flag.
    /// </remarks>
    public static async Task SoftDeleteUserAsync(IServiceProvider services, string userId)
    {
        var dbContext = services.GetRequiredService<IdentityDbContext>();

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Id == userId);

        user.SoftDelete();
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// The stored SHA-256 hashes of a user's refresh tokens (Admin panel S6).
    /// </summary>
    /// <remarks>
    /// Exists for one assertion: that <c>GET /api/v1/admin/users/{id}/sessions</c> does not contain
    /// any of them. Checking the field NAMES is not enough on its own — a rename would defeat it —
    /// so the test compares against the actual values.
    /// </remarks>
    public static async Task<List<string>> GetRefreshTokenHashesAsync(IServiceProvider services, string userId)
    {
        var dbContext = services.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.TokenHash)
            .ToListAsync();
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
