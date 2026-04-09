namespace EShop.ApiGateway.Configuration;

public sealed class IdentityServiceOptions
{
    public const string SectionName = "IdentityService";

    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyHeaderName { get; set; } = "X-Internal-Api-Key";
}
