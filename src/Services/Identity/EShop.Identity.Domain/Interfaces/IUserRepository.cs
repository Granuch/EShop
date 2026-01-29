using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Repository for user-related operations
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Gets a user by their ID
    /// </summary>
    Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a user by their email
    /// </summary>
    Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a user by OAuth provider ID
    /// </summary>
    Task<ApplicationUser?> GetByOAuthProviderAsync(string provider, string providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user with password
    /// </summary>
    Task<ApplicationUser> CreateAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing user
    /// </summary>
    Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Soft deletes a user
    /// </summary>
    Task DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user to a role
    /// </summary>
    Task AddToRoleAsync(ApplicationUser user, string role, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all roles for a user
    /// </summary>
    Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}
