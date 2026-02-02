using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
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

    public async Task<RefreshTokenEntity?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token, cancellationToken);
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

            await _context.SaveChangesAsync(cancellationToken);
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

    public async Task<int> RevokeTokenAtomicallyAsync(
        string token,
        DateTime revokedAt,
        string? revokedByIp,
        string? replacedByToken,
        string revokeReason,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsInMemory())
        {
            var refreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == token && t.RevokedAt == null && t.ExpiresAt > revokedAt, cancellationToken);

            if (refreshToken == null)
                return 0;

            refreshToken.RevokedAt = revokedAt;
            refreshToken.RevokedByIp = revokedByIp;
            refreshToken.ReplacedByToken = replacedByToken;
            refreshToken.RevokeReason = revokeReason;

            await _context.SaveChangesAsync(cancellationToken);
            return 1;
        }
        else
        {
            var trackedEntity = _context.RefreshTokens.Local
                .FirstOrDefault(t => t.Token == token);

            if (trackedEntity != null)
            {
                _context.Entry(trackedEntity).State = EntityState.Detached;
            }

            return await _context.RefreshTokens
                .Where(t => t.Token == token && t.RevokedAt == null && t.ExpiresAt > revokedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.RevokedAt, revokedAt)
                    .SetProperty(t => t.RevokedByIp, revokedByIp)
                    .SetProperty(t => t.ReplacedByToken, replacedByToken)
                    .SetProperty(t => t.RevokeReason, revokeReason),
                    cancellationToken);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
