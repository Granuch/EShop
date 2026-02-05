namespace EShop.Identity.Infrastructure.Services;

/// <summary>
/// Service for cleaning up expired and revoked refresh tokens
/// </summary>
public interface ITokenCleanupService
{
    /// <summary>
    /// Clean up expired and revoked tokens older than the retention period
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of tokens deleted</returns>
    Task<int> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);
}
