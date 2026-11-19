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

    /// <summary>
    /// At most one notice per event type and route is sent in this many seconds; the rest are counted, and the next
    /// notice that goes out says how many were held back. Until this existed every 429 sent its own email: on
    /// 2026-11-18 a 10-minute burst of 642 catalog 429s sent ~550 through the shop's Gmail account and used up its
    /// daily quota, so customer confirmations failed for a day. 0 turns the window off.
    /// </summary>
    public int EmailNotificationWindowSeconds { get; set; } = 900;

    /// <summary>
    /// The most notices sent per UTC day across every event type and route, so no burst can exhaust a mailbox.
    /// 0 removes the cap; to send nothing, leave <see cref="OperationsEmailRecipients"/> empty instead.
    /// </summary>
    public int MaxEmailNotificationsPerDay { get; set; } = 50;

    public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";
    public string UserIdHeaderName { get; set; } = "X-User-Id";
}
