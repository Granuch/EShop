namespace EShop.Notification.Infrastructure.Configuration;

public sealed class IdentityServiceSettings
{
    public const string SectionName = "IdentityService";

    public string BaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 5;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiKeyHeaderName { get; init; } = "X-Internal-Api-Key";

    /// <summary>The delay before the contact lookup's second attempt; it doubles before the third.</summary>
    public int RetryBaseDelayMilliseconds { get; init; } = 1000;
}
