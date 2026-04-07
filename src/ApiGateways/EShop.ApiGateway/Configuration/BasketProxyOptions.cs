namespace EShop.ApiGateway.Configuration;

public sealed class BasketProxyOptions
{
    public const string SectionName = "BasketProxy";

    public long MaxRequestBodySizeBytes { get; set; } = 1_048_576;
    public int UpstreamUnavailableRetryAfterSeconds { get; set; } = 5;
}
