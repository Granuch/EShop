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
    private const int MaxActiveTokensReturned = 100;

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

        // Tracked, and Include(User) stays: ValidateRefreshTokenAsync returns refreshToken.User
        // to its caller, and RevokeTokenAsync mutates the entity it gets back from here. Both
        // would break under AsNoTracking or without the graph.
        return await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    /// <summary>
    /// Read-only listing, so no tracking, and bounded so a user with a pathological number of
    /// live sessions cannot pull the whole set into memory. Note this currently has **no
    /// callers** anywhere in src or tests — the hardening is defensive, for whoever wires it up.
    /// </summary>
    public async Task<IEnumerable<RefreshTokenEntity>> GetActiveTokensByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .Take(MaxActiveTokensReturned)
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

        // One server-side UPDATE. This used to be forked on `_context.Database.IsInMemory()` so
        // tests could run a tracked read-modify loop instead — which meant the shipped branch had
        // no coverage at all, and the two branches were not equivalent (the tracked loop relies on
        // the caller committing; this does not). The fork is gone and
        // RefreshTokenRepositoryPostgresTests covers this path on a real database.
        await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedByIp, ipAddress)
                .SetProperty(t => t.RevokeReason, revokeReason),
                cancellationToken);
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
        // Detach first: the row is about to be updated server-side, so a tracked copy would go
        // stale and could later be written back over this update.
        var trackedEntity = _context.RefreshTokens.Local
            .FirstOrDefault(t => t.TokenHash == tokenHash);

        if (trackedEntity != null)
        {
            _context.Entry(trackedEntity).State = EntityState.Detached;
        }

        // The WHERE clause is the atomicity guarantee: a token that is already revoked or expired
        // matches nothing and the caller gets 0, which is what stops a refresh-token replay from
        // succeeding twice. Previously forked on IsInMemory() — the tracked branch tests took
        // could return 1 in races where this correctly returns 0, so the guarantee itself was
        // untested. See RefreshTokenRepositoryPostgresTests.
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
