using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.Infrastructure.Repositories;

/// <summary>
/// Repository for user-related operations
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IdentityDbContext _dbContext;

    public UserRepository(
        UserManager<ApplicationUser> userManager,
        IdentityDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    public async Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _userManager.FindByIdAsync(userId);
    }

    public async Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _userManager.FindByEmailAsync(email);
    }

    public async Task<ApplicationUser?> GetByOAuthProviderAsync(string provider, string providerId, CancellationToken cancellationToken = default)
    {
        return provider.ToLower() switch
        {
            "google" => await _userManager.Users.FirstOrDefaultAsync(u => u.GoogleId == providerId, cancellationToken),
            "github" => await _userManager.Users.FirstOrDefaultAsync(u => u.GitHubId == providerId, cancellationToken),
            _ => null
        };
    }

    public async Task<ApplicationUser> CreateAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create user: {errors}");
        }
        return user;
    }

    public async Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to update user: {errors}");
        }
    }

    public async Task DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.IsActive = false;
        await UpdateAsync(user, cancellationToken);
    }

    public async Task AddToRoleAsync(ApplicationUser user, string role, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to add role: {errors}");
        }
    }

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        // Use EF Core directly to avoid N+1 when called in loops
        // This executes a single query joining user_roles and roles tables
        var roles = await _dbContext.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(
                _dbContext.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => r.Name!)
            .ToListAsync(cancellationToken);

        return roles;
    }

    public async Task<IDictionary<string, IReadOnlyList<string>>> GetRolesForUsersAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var userIdList = userIds.ToList();
        if (!userIdList.Any())
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        // Execute a single query to get all user-role mappings for multiple users
        // This prevents N+1 queries when loading roles for user lists
        var userRoles = await _dbContext.UserRoles
            .Where(ur => userIdList.Contains(ur.UserId))
            .Join(
                _dbContext.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, RoleName = r.Name! })
            .ToListAsync(cancellationToken);

        // Group by user ID and return as dictionary
        var result = userRoles
            .GroupBy(ur => ur.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.RoleName).ToList());

        // Ensure all requested users are in the result (even if they have no roles)
        foreach (var userId in userIdList)
        {
            if (!result.ContainsKey(userId))
            {
                result[userId] = Array.Empty<string>();
            }
        }

        return result;
    }
}
