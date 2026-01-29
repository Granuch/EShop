using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.Infrastructure.Repositories;

/// <summary>
/// Repository for user-related operations
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserRepository(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
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
        return await _userManager.GetRolesAsync(user);
    }
}
