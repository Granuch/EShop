namespace EShop.ApiGateway.Configuration;

public sealed class NotificationProxyOptions
{
    public const string SectionName = "NotificationProxy";

    public long MaxRequestBodySizeBytes { get; set; } = 1_048_576;
    public int UpstreamUnavailableRetryAfterSeconds { get; set; } = 5;
}
