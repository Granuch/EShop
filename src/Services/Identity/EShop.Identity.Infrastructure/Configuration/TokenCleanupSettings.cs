namespace EShop.Identity.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for the Expired Token Cleanup background service
/// </summary>
public class TokenCleanupSettings
{
    public const string SectionName = "TokenCleanup";

    /// <summary>
    /// How often the cleanup job should run (in hours)
    /// Default: 24 hours (once per day)
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 24;

    /// <summary>
    /// How many days to retain expired/revoked tokens before deletion
    /// Default: 30 days
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Delay before first cleanup run after service starts (in minutes)
    /// Default: 1 minute
    /// </summary>
    public int InitialDelayMinutes { get; set; } = 1;

    /// <summary>
    /// Enable or disable the cleanup service
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
}
