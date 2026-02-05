namespace EShop.Identity.Domain.Security;

/// <summary>
/// Represents a failed login attempt with tracking information.
/// </summary>
public record LoginAttempt
{
    /// <summary>
    /// Timestamp of the attempt (UTC)
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// IP address from which the attempt originated
    /// </summary>
    public string IpAddress { get; init; } = string.Empty;

    /// <summary>
    /// Hashed identifier (username/email) for privacy
    /// </summary>
    public string HashedIdentifier { get; init; } = string.Empty;
}

/// <summary>
/// Result of login attempt validation including throttling and lockout information.
/// </summary>
public record LoginAttemptValidationResult
{
    /// <summary>
    /// Whether the login attempt is allowed to proceed
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// If blocked, the reason for blocking
    /// </summary>
    public BlockReason? BlockReason { get; init; }

    /// <summary>
    /// If throttled, the delay in seconds before retry is allowed
    /// </summary>
    public int? ThrottleDelaySeconds { get; init; }

    /// <summary>
    /// Number of failed attempts for this account
    /// </summary>
    public int FailedAttempts { get; init; }

    /// <summary>
    /// Time until lockout/block expires (if applicable)
    /// </summary>
    public DateTime? BlockExpiresAt { get; init; }

    /// <summary>
    /// Whether this is a suspicious pattern (e.g., many IPs)
    /// </summary>
    public bool IsSuspicious { get; init; }

    /// <summary>
    /// Additional context for logging/metrics
    /// </summary>
    public string? Message { get; init; }

    public static LoginAttemptValidationResult Allowed() =>
        new() { IsAllowed = true };

    public static LoginAttemptValidationResult Throttled(int delaySeconds, int failedAttempts, string message) =>
        new()
        {
            IsAllowed = false,
            BlockReason = Security.BlockReason.Throttled,
            ThrottleDelaySeconds = delaySeconds,
            FailedAttempts = failedAttempts,
            Message = message
        };

    public static LoginAttemptValidationResult Blocked(
        BlockReason reason,
        int failedAttempts,
        DateTime? expiresAt,
        string message,
        bool isSuspicious = false) =>
        new()
        {
            IsAllowed = false,
            BlockReason = reason,
            FailedAttempts = failedAttempts,
            BlockExpiresAt = expiresAt,
            Message = message,
            IsSuspicious = isSuspicious
        };
}

/// <summary>
/// Reason why a login attempt was blocked.
/// </summary>
public enum BlockReason
{
    /// <summary>
    /// Account temporarily locked due to repeated failed attempts
    /// </summary>
    AccountLocked,

    /// <summary>
    /// IP address blocked due to excessive failed attempts
    /// </summary>
    IpBlocked,

    /// <summary>
    /// Progressive throttling applied (delay required)
    /// </summary>
    Throttled,

    /// <summary>
    /// Distributed attack pattern detected (many IPs targeting account)
    /// </summary>
    DistributedAttackDetected
}
