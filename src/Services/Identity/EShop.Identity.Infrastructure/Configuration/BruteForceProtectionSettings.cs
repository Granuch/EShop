namespace EShop.Identity.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for brute-force protection.
/// Implements defense-in-depth strategy with multiple layers of protection.
/// </summary>
public class BruteForceProtectionSettings
{
    public const string SectionName = "BruteForceProtection";

    /// <summary>
    /// Maximum failed login attempts per account before applying progressive throttling.
    /// Default: 3 attempts before throttling begins
    /// </summary>
    public int MaxFailedAttemptsBeforeThrottle { get; init; } = 3;

    /// <summary>
    /// Maximum failed login attempts per account before temporary lockout.
    /// Default: 5 attempts
    /// </summary>
    public int MaxFailedAttemptsBeforeLockout { get; init; } = 5;

    /// <summary>
    /// Maximum failed login attempts from a single IP before blocking.
    /// Defense against single-IP brute force attacks.
    /// Default: 10 attempts
    /// </summary>
    public int MaxFailedAttemptsPerIp { get; init; } = 10;

    /// <summary>
    /// Maximum distinct IPs attempting to login to a single account before alerting.
    /// Defense against distributed/rotating-IP brute force attacks.
    /// Default: 5 IPs
    /// </summary>
    public int MaxDistinctIpsPerAccount { get; init; } = 5;

    /// <summary>
    /// Time window for tracking login attempts (in minutes).
    /// Default: 15 minutes - sliding window
    /// </summary>
    public int AttemptTrackingWindowMinutes { get; init; } = 15;

    /// <summary>
    /// Base delay for progressive throttling (in seconds).
    /// Actual delay = BaseDelay * 2^(attempts - threshold)
    /// Default: 2 seconds
    /// </summary>
    public int ProgressiveThrottleBaseDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Maximum throttle delay (in seconds).
    /// Prevents excessive waits while still deterring attackers.
    /// Default: 30 seconds
    /// </summary>
    public int MaxThrottleDelaySeconds { get; init; } = 30;

    /// <summary>
    /// Temporary lockout duration for account-level protection (in minutes).
    /// Separate from ASP.NET Identity lockout for additional protection.
    /// Default: 10 minutes
    /// </summary>
    public int TemporaryLockoutMinutes { get; init; } = 10;

    /// <summary>
    /// Duration to cache IP blocks (in minutes).
    /// Default: 30 minutes
    /// </summary>
    public int IpBlockDurationMinutes { get; init; } = 30;

    /// <summary>
    /// Minimum response time for login attempts (in milliseconds).
    /// Used to prevent account enumeration through timing attacks.
    /// All login responses will take at least this long.
    /// Default: 800ms
    /// </summary>
    public int MinimumResponseTimeMs { get; init; } = 800;

    /// <summary>
    /// Maximum response time variation (in milliseconds).
    /// Random variation added to minimum response time.
    /// Default: 400ms (total range: 800-1200ms)
    /// </summary>
    public int ResponseTimeVariationMs { get; init; } = 400;

    /// <summary>
    /// Enable enhanced logging for suspicious patterns.
    /// Logs when distributed attacks are detected.
    /// Default: true
    /// </summary>
    public bool EnableSuspiciousPatternLogging { get; init; } = true;

    /// <summary>
    /// Cache key prefix for login attempts.
    /// Used for Redis/distributed cache organization.
    /// </summary>
    public string CacheKeyPrefix { get; init; } = "bf_protection";
}
