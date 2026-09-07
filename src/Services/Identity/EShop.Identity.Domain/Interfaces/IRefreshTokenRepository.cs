using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Repository for refresh token operations
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Looks up a token by the <b>plaintext</b> value the client presented; the hashing happens
    /// inside the repository. Never pass a hash here — it would be hashed again and never match.
    /// </summary>
    Task<RefreshTokenEntity?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<IEnumerable<RefreshTokenEntity>> GetActiveTokensByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default);
    Task UpdateAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default);
    Task RevokeAllUserTokensAsync(string userId, string? reason = null, string? ipAddress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a token addressed by its <b>hash</b>, not its plaintext — hence the name. Rotation
    /// is the only caller and it holds a <see cref="RefreshTokenEntity"/>, which no longer knows
    /// the plaintext at all; the method beside it, <see cref="GetByTokenAsync"/>, deliberately
    /// takes the opposite. Getting these two the wrong way round fails silently: a double-hashed
    /// or un-hashed lookup simply matches no row, and rotation reports "token already used".
    /// </summary>
    Task<int> RevokeTokenByHashAtomicallyAsync(string tokenHash, DateTime revokedAt, string? revokedByIp, string? replacedByTokenHash, string revokeReason, CancellationToken cancellationToken = default);
}
