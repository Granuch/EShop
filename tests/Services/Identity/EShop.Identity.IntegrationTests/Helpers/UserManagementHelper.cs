using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Helpers;

/// <summary>
/// Helper methods for managing test users.
/// </summary>
/// <remarks>
/// <b>Every method opens its own DI scope, and that is load-bearing (Admin panel S7).</b> Callers
/// pass <c>Factory.Services</c>, the root provider — resolving a scoped <c>IdentityDbContext</c>
/// from it creates one in the <i>root</i> scope, which then lives for the whole host. Under the
/// fixture-scoped host (PERF-02) that is the whole fixture, so one long-lived change tracker
/// accumulated every user any test ever created, while the API mutated those same rows through its
/// own request scopes and bumped their <c>ConcurrencyStamp</c>. The tracked copies kept the old
/// stamp, EF's identity resolution handed a later query the stale instance rather than refreshing
/// it, and the next <c>SaveChangesAsync</c> — including one belonging to a completely different
/// test — failed with <c>DbUpdateConcurrencyException: expected to affect 1 row(s), but actually
/// affected 0</c>.
/// <para>
/// It surfaced as six failures that all passed in isolation, attributed to whichever test happened
/// to trigger the flush rather than to the one that left the stale entity. A fresh scope per call
/// means nothing outlives the helper.
/// </para>
/// </remarks>
public static class UserManagementHelper
{
    /// <summary>
    /// A short-lived scope, whoever the caller resolved from — creating one from a scope's own
    /// provider is legal and still gives a fresh <c>IdentityDbContext</c>.
    /// </summary>
    private static IServiceScope NewScope(IServiceProvider services)
        => services.GetRequiredService<IServiceScopeFactory>().CreateScope();

    public static async Task<string> CreateTestUserAsync(
        IServiceProvider services,
        string? email = null,
        string? password = null,
        string role = TestUsers.Roles.User,
        bool emailConfirmed = true,
        bool isActive = true)
    {
        using var scope = NewScope(services);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        
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
        using var scope = NewScope(services);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
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
        using var scope = NewScope(services);
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

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
        using var scope = NewScope(services);
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.TokenHash)
            .ToListAsync();
    }

    public static async Task DeleteTestUserAsync(IServiceProvider services, string userId)
    {
        using var scope = NewScope(services);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        
        if (user != null)
        {
            await userManager.DeleteAsync(user);
        }
    }

    /// <summary>
    /// Turns two-factor authentication on for a user without going through enrolment (Admin panel
    /// S7).
    /// </summary>
    /// <remarks>
    /// The real path needs a valid TOTP code from an authenticator app, which a test cannot
    /// produce, so the flag is set directly. That is enough for the admin disable endpoint, whose
    /// whole point is that it does <b>not</b> ask for a code.
    /// </remarks>
    public static async Task SetTwoFactorEnabledAsync(IServiceProvider services, string userId, bool enabled)
    {
        using var scope = NewScope(services);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' was not found.");

        var result = await userManager.SetTwoFactorEnabledAsync(user, enabled);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to set 2FA: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    /// <summary>
    /// The stored <c>AccessFailedCount</c> (Admin panel S7), read straight from the row so an
    /// unlock's effect can be asserted rather than inferred from a status code.
    /// </summary>
    public static async Task<int> GetAccessFailedCountAsync(IServiceProvider services, string userId)
    {
        using var scope = NewScope(services);
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => u.AccessFailedCount)
            .SingleAsync();
    }

    /// <summary>
    /// Sets <c>AccessFailedCount</c> directly (Admin panel S7), so the unlock test has something to
    /// clear. Nothing in the login path increments it — brute force is tracked in Redis by
    /// <c>ILoginAttemptTracker</c> instead — so there is no way to arrange this through the API.
    /// </summary>
    public static async Task SetAccessFailedCountAsync(IServiceProvider services, string userId, int count)
    {
        using var scope = NewScope(services);
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Id == userId);

        user.AccessFailedCount = count;
        await dbContext.SaveChangesAsync();
    }

    public static async Task<string?> GetUserIdByEmailAsync(IServiceProvider services, string email)
    {
        using var scope = NewScope(services);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user?.Id;
    }

    public static async Task ChangeUserPasswordAsync(
        IServiceProvider services,
        string userId,
        string newPassword)
    {
        using var scope = NewScope(services);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
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
