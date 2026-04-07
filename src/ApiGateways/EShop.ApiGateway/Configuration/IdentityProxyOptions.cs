namespace EShop.ApiGateway.Configuration;

public sealed class IdentityProxyOptions
{
    public const string SectionName = "IdentityProxy";

    public long MaxRequestBodySizeBytes { get; set; } = 1_048_576;
    public int UpstreamUnavailableRetryAfterSeconds { get; set; } = 5;
}
