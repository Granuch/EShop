namespace EShop.ApiGateway.Configuration;

public sealed class CatalogProxyOptions
{
    public const string SectionName = "CatalogProxy";

    public long MaxRequestBodySizeBytes { get; set; } = 1_048_576;
    public int UpstreamUnavailableRetryAfterSeconds { get; set; } = 5;
}
