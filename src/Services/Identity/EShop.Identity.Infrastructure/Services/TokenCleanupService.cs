using EShop.Identity.Infrastructure.Configuration;
using EShop.Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Identity.Infrastructure.Services;

/// <summary>
/// Service for cleaning up expired and revoked refresh tokens from the database
/// </summary>
public class TokenCleanupService : ITokenCleanupService
{
    private readonly IdentityDbContext _context;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TokenCleanupSettings _settings;

    public TokenCleanupService(
        IdentityDbContext context,
        ILogger<TokenCleanupService> logger,
        IOptions<TokenCleanupSettings> settings)
    {
        _context = context;
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Clean up expired and revoked tokens older than the retention period
    /// </summary>
    public async Task<int> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.RetentionDays);

        _logger.LogInformation(
            "Starting cleanup of expired refresh tokens older than {CutoffDate} ({RetentionDays} days)",
            cutoffDate,
            _settings.RetentionDays);

        try
        {
            // Delete tokens that are either:
            // 1. Expired and older than retention period
            // 2. Revoked and older than retention period

            // Note: ExecuteDeleteAsync is more efficient for real databases (PostgreSQL),
            // but we use ToListAsync + RemoveRange for compatibility with InMemory provider in tests
            var tokensToDelete = await _context.RefreshTokens
                .Where(t => t.ExpiresAt < cutoffDate ||
                           (t.RevokedAt != null && t.RevokedAt < cutoffDate))
                .ToListAsync(cancellationToken);

            if (tokensToDelete.Count > 0)
            {
                _context.RefreshTokens.RemoveRange(tokensToDelete);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully cleaned up {DeletedCount} expired/revoked refresh tokens",
                    tokensToDelete.Count);
            }
            else
            {
                _logger.LogInformation("No expired refresh tokens found for cleanup");
            }

            return tokensToDelete.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clean up expired refresh tokens. Cutoff date: {CutoffDate}",
                cutoffDate);
            throw;
        }
    }
}
