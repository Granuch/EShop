namespace EShop.ApiGateway.Configuration;

public sealed class OrderingProxyOptions
{
    public const string SectionName = "OrderingProxy";

    public long MaxRequestBodySizeBytes { get; set; } = 1_048_576;
    public int UpstreamUnavailableRetryAfterSeconds { get; set; } = 5;
}
