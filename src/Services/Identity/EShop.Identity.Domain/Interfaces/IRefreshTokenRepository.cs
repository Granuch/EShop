using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Repository for refresh token operations
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshTokenEntity?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<IEnumerable<RefreshTokenEntity>> GetActiveTokensByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default);
    Task UpdateAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default);
    Task RevokeAllUserTokensAsync(string userId, string? reason = null, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<int> RevokeTokenAtomicallyAsync(string token, DateTime revokedAt, string? revokedByIp, string? replacedByToken, string revokeReason, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
