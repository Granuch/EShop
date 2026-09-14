namespace EShop.Notification.Infrastructure.Configuration;

/// <summary>How long <c>NotificationLogs</c> rows are kept (Notification audit S5, M8, D8).</summary>
public sealed class NotificationLogRetentionSettings
{
    public const string SectionName = "NotificationLogRetention";

    /// <summary>Rows whose last update is older than this are deleted. At least 1.</summary>
    public int RetentionDays { get; init; } = 90;

    public int CleanupIntervalHours { get; init; } = 6;

    /// <summary>Rows deleted per statement, so one run never holds a long lock.</summary>
    public int BatchSize { get; init; } = 1000;
}
