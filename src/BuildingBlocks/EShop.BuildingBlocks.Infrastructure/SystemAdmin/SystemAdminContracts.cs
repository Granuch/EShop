namespace EShop.BuildingBlocks.Infrastructure.SystemAdmin;

/// <summary>
/// The admin panel's System page (S19, endpoints #85, #87–89). One path per concern, shared by the gateway and the
/// services that contribute to it.
///
/// <para>
/// <b>Settings and feature flags follow the audit trail's shape (S15):</b> the gateway serves the path itself and asks
/// each contributing service for the same path, with the caller's token, then composes the answers. The per-service
/// endpoints are therefore not routed by the gateway at all. Health is gateway-served too, but reads each service's
/// anonymous <c>/health</c>. Cache invalidation is the one that is proxied, to Catalog — the only service with
/// service-wide cache families.
/// </para>
/// </summary>
public static class SystemAdminPaths
{
    public const string Settings = "/api/v1/admin/settings";
    public const string FeatureFlags = "/api/v1/admin/feature-flags";
    public const string Health = "/api/v1/admin/health";
    public const string CacheInvalidate = "/api/v1/admin/cache/invalidate";
}

/// <summary>
/// Ordering's contribution to the settings page: how an order is priced (decision Q9c, read-only).
///
/// <para>
/// <b>Tax and shipping are reported as absent, not left out.</b> The plan expected a tax rate, a shipping cost and a
/// free-shipping threshold "as currently configured"; none of them exists anywhere in the code — an order's total is
/// the sum of its lines — and the currency is a compiled constant, not configuration. A settings page that omitted
/// the two fields would let a reader assume they are configured somewhere else; <c>false</c> says they are not
/// applied at all.
/// </para>
/// </summary>
/// <param name="Currency">The currency every order is priced in (<c>Order.PricingCurrency</c>).</param>
/// <param name="TaxApplied">Whether any tax is added to an order's lines. <c>false</c>: none is modelled.</param>
/// <param name="ShippingCharged">Whether any shipping cost is added. <c>false</c>: none is modelled.</param>
public sealed record PricingSettingsDto(string Currency, bool TaxApplied, bool ShippingCharged);

/// <summary>Payment's contribution to the settings page: which provider takes the money.</summary>
/// <param name="Provider"><see cref="PaymentProviders.Stripe"/> or <see cref="PaymentProviders.Simulator"/>.</param>
public sealed record PaymentSettingsDto(string Provider);

/// <summary>The values of <see cref="PaymentSettingsDto.Provider"/>.</summary>
public static class PaymentProviders
{
    /// <summary><c>Stripe:Enabled</c> is true: customers pay through Stripe.</summary>
    public const string Stripe = "Stripe";

    /// <summary><c>Stripe:Enabled</c> is false: every order is settled by the payment simulator.</summary>
    public const string Simulator = "Simulator";
}

/// <summary>
/// Payment's contribution to the feature-flags page (#89): the payment simulator and the webhook signature bypass, as
/// configured. Read-only (decision taken at S19, by the same reasoning as Q9c): changing one stays a redeploy.
/// </summary>
/// <param name="SimulatorActive">
/// Whether the simulator settles orders at all — true exactly when Stripe is off. The simulation values below are
/// reported either way, so the page can show what would apply.
/// </param>
/// <param name="SimulationMode"><c>Random</c>, <c>AlwaysSuccess</c> or <c>AlwaysFailure</c>.</param>
/// <param name="SuccessRatePercent">The chance of success in <c>Random</c> mode.</param>
/// <param name="ProcessingDelayMinSeconds">The simulated processing delay's lower bound.</param>
/// <param name="ProcessingDelayMaxSeconds">The simulated processing delay's upper bound.</param>
/// <param name="RefundDelaySeconds">The simulated refund delay.</param>
/// <param name="WebhookSignatureVerificationSkipped">
/// <c>Stripe:SkipWebhookSignatureVerification</c>. Startup refuses it outside Development, Sandbox and Testing, so a
/// running service reporting <c>true</c> is in one of those.
/// </param>
public sealed record PaymentFeatureFlagsDto(
    bool SimulatorActive,
    string SimulationMode,
    int SuccessRatePercent,
    int ProcessingDelayMinSeconds,
    int ProcessingDelayMaxSeconds,
    int RefundDelaySeconds,
    bool WebhookSignatureVerificationSkipped);
