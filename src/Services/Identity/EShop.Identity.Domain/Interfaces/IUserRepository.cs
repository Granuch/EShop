using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Repository for user-related operations
/// </summary>
public interface IUserRepository
{
    // TODO: Implement user queries
    Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetByOAuthProviderAsync(string provider, string providerId, CancellationToken cancellationToken = default);

    // TODO: Implement user commands
    Task<ApplicationUser> CreateAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default);
    Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default);
    Task DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    // TODO: Implement role management
    Task AddToRoleAsync(ApplicationUser user, string role, CancellationToken cancellationToken = default);
    Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}
