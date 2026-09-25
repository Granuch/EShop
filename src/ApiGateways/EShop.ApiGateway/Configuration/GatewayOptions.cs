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

    /// <summary>
    /// Who receives the gateway's operational notices: failures, rate limiting, simulated failures and critical
    /// operations. Operators only — never the user whose request caused the notice (frontend-contracts F-55). Until
    /// that fix every notice went to the caller, so a customer paying for an order was emailed route ids and correlation
    /// ids for each request. Empty (the default) means no notice is sent at all.
    /// </summary>
    public string[] OperationsEmailRecipients { get; set; } = [];

    /// <summary>The configured recipients that are usable addresses, trimmed and de-duplicated.</summary>
    public IReadOnlyList<string> EffectiveOperationsEmailRecipients =>
        OperationsEmailRecipients
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";
    public string UserIdHeaderName { get; set; } = "X-User-Id";
}
