namespace EShop.Identity.Domain.Security;

/// <summary>
/// Service for tracking and validating login attempts to prevent brute-force attacks.
/// Implements defense-in-depth strategy with multiple protection layers.
/// 
/// Protection layers:
/// 1. Account-level tracking: Monitors attempts per username/email
/// 2. IP-level tracking: Monitors attempts per source IP
/// 3. Composite tracking: Monitors account+IP combinations
/// 4. Distributed attack detection: Monitors multiple IPs per account
/// 5. Progressive throttling: Exponential backoff for repeated failures
/// 6. Temporary lockouts: Time-based access restrictions
/// 
/// Privacy considerations:
/// - All identifiers are hashed before storage/logging (SHA256)
/// - Raw usernames/emails never stored in cache or logs
/// - IP addresses logged only for operational security
/// 
/// Scalability:
/// - Uses IDistributedCache for horizontal scaling
/// - Works across multiple service instances
/// - Redis recommended for production
/// </summary>
public interface ILoginAttemptTracker
{
    /// <summary>
    /// Validates whether a login attempt should be allowed based on historical attempts.
    /// Checks all protection layers and returns detailed validation result.
    /// </summary>
    /// <param name="identifier">Username or email (will be hashed internally)</param>
    /// <param name="ipAddress">Source IP address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with throttling/blocking information</returns>
    Task<LoginAttemptValidationResult> ValidateAttemptAsync(
        string identifier,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed login attempt.
    /// Updates all tracking layers and triggers alerts if thresholds exceeded.
    /// </summary>
    /// <param name="identifier">Username or email (will be hashed internally)</param>
    /// <param name="ipAddress">Source IP address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordFailedAttemptAsync(
        string identifier,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful login.
    /// Clears failed attempt counters for the account.
    /// </summary>
    /// <param name="identifier">Username or email (will be hashed internally)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordSuccessfulLoginAsync(
        string identifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually resets all tracking for a specific account.
    /// Used by administrators or after account verification.
    /// </summary>
    /// <param name="identifier">Username or email (will be hashed internally)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ResetAccountAttemptsAsync(
        string identifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current failed attempt count for an account.
    /// Used for metrics and monitoring.
    /// </summary>
    /// <param name="identifier">Username or email (will be hashed internally)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of failed attempts in current tracking window</returns>
    Task<int> GetFailedAttemptCountAsync(
        string identifier,
        CancellationToken cancellationToken = default);
}
