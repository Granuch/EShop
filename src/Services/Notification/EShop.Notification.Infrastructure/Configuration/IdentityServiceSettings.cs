namespace EShop.Notification.Infrastructure.Configuration;

public sealed class IdentityServiceSettings
{
    public const string SectionName = "IdentityService";

    public string BaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 5;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiKeyHeaderName { get; init; } = "X-Internal-Api-Key";
}
