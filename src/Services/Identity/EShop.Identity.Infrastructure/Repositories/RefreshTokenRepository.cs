using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
using EShop.Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.Infrastructure.Repositories;

/// <summary>
/// Repository for refresh token operations
/// </summary>
public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _context;

    public RefreshTokenRepository(IdentityDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Takes the plaintext token the client presented and hashes it here, so hashing lives at
    /// exactly one boundary and no caller has to remember to do it. A caller that hashed first
    /// and passed the digest would double-hash and silently never match.
    /// </summary>
    public async Task<RefreshTokenEntity?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokenHash = RefreshTokenHasher.Hash(token);

        return await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<IEnumerable<RefreshTokenEntity>> GetActiveTokensByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default)
    {
        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
    }

    public Task UpdateAsync(RefreshTokenEntity refreshToken, CancellationToken cancellationToken = default)
    {
        _context.RefreshTokens.Update(refreshToken);
        return Task.CompletedTask;
    }

    public async Task RevokeAllUserTokensAsync(string userId, string? reason = null, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var revokeReason = reason ?? "Revoked by user";

        if (_context.Database.IsInMemory())
        {
            var activeTokens = await _context.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in activeTokens)
            {
                token.RevokedAt = now;
                token.RevokedByIp = ipAddress;
                token.RevokeReason = revokeReason;
            }

            // Entities are already tracked by EF Core, changes will be saved by caller
        }
        else
        {
            await _context.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedByIp, ipAddress)
                    .SetProperty(t => t.RevokeReason, revokeReason),
                    cancellationToken);
        }
    }

    /// <summary>
    /// Addressed by hash, unlike <see cref="GetByTokenAsync"/> — see the interface for why.
    /// Note <c>replacedByTokenHash</c> is a hash for the same reason the token is: this column
    /// used to store the *new* token in the clear on the old row, so the rotation chain leaked
    /// successor tokens as well as the current one.
    /// </summary>
    public async Task<int> RevokeTokenByHashAtomicallyAsync(
        string tokenHash,
        DateTime revokedAt,
        string? revokedByIp,
        string? replacedByTokenHash,
        string revokeReason,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsInMemory())
        {
            var refreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > revokedAt, cancellationToken);

            if (refreshToken == null)
                return 0;

            refreshToken.RevokedAt = revokedAt;
            refreshToken.RevokedByIp = revokedByIp;
            refreshToken.ReplacedByTokenHash = replacedByTokenHash;
            refreshToken.RevokeReason = revokeReason;

            // Entity is already tracked by EF Core, changes will be saved by CommitTransaction
            return 1;
        }
        else
        {
            var trackedEntity = _context.RefreshTokens.Local
                .FirstOrDefault(t => t.TokenHash == tokenHash);

            if (trackedEntity != null)
            {
                _context.Entry(trackedEntity).State = EntityState.Detached;
            }

            return await _context.RefreshTokens
                .Where(t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > revokedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.RevokedAt, revokedAt)
                    .SetProperty(t => t.RevokedByIp, revokedByIp)
                    .SetProperty(t => t.ReplacedByTokenHash, replacedByTokenHash)
                    .SetProperty(t => t.RevokeReason, revokeReason),
                    cancellationToken);
        }
    }
}
