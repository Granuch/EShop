namespace EShop.ApiGateway.Configuration;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public bool EnableAuditEmailNotifications { get; set; }
    public bool EnableSimulationFailureEmailNotifications { get; set; } = true;
    public bool EnableProxyFailureEmailNotifications { get; set; } = true;
    public bool EnableRateLimitEmailNotifications { get; set; } = true;
    public bool EnableCriticalSuccessEmailNotifications { get; set; }
    public string[] CriticalSuccessPathPrefixes { get; set; } = [];
    public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";
    public string UserIdHeaderName { get; set; } = "X-User-Id";
}
