using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.SystemAdmin;
using EShop.Payment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace EShop.Payment.API.Endpoints;

/// <summary>
/// Payment's slices of the System page (admin panel S19): the payment provider for the read-only settings (#85, Q9c),
/// and the simulator and webhook bypass for the read-only feature flags (#89). The gateway serves both paths itself and
/// asks these endpoints with the caller's token; it does not route either path here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Configuration, reported — never a secret.</b> Both answers are built from typed records that have no field for a
/// Stripe key or the webhook secret, so a key cannot reach the body by a projection mistake. <c>PaymentSystemTests</c>
/// sets known key values on its host and asserts they are absent from both bodies by value.
/// </para>
/// <para>
/// <c>GET /api/v1/payments/simulation</c> (the older diagnostics endpoint, <c>Admin</c> role) overlaps the flags answer
/// and is left as it is: it is an existing contract, and this stage adds rather than moves.
/// </para>
/// </remarks>
public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(SystemAdminPaths.Settings, (IOptions<StripeSettings> stripe) =>
                Results.Ok(new PaymentSettingsDto(
                    stripe.Value.Enabled ? PaymentProviders.Stripe : PaymentProviders.Simulator)))
            .RequireAuthorization(EShopPermissions.SystemManage)
            .WithName("GetPaymentSettings")
            .WithTags("Admin — System")
            .Produces<PaymentSettingsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet(SystemAdminPaths.FeatureFlags, (
                IOptions<StripeSettings> stripe,
                IOptions<PaymentSimulationSettings> simulation) =>
            {
                var s = simulation.Value;

                return Results.Ok(new PaymentFeatureFlagsDto(
                    // OrderCreatedConsumer's own rule (Payment audit D1): Stripe off means the simulator settles.
                    SimulatorActive: !stripe.Value.Enabled,
                    SimulationMode: s.Mode.ToString(),
                    SuccessRatePercent: s.SuccessRatePercent,
                    ProcessingDelayMinSeconds: s.ProcessingDelayMinSeconds,
                    ProcessingDelayMaxSeconds: s.ProcessingDelayMaxSeconds,
                    RefundDelaySeconds: s.RefundDelaySeconds,
                    WebhookSignatureVerificationSkipped: stripe.Value.SkipWebhookSignatureVerification));
            })
            .RequireAuthorization(EShopPermissions.SystemManage)
            .WithName("GetPaymentFeatureFlags")
            .WithTags("Admin — System")
            .Produces<PaymentFeatureFlagsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
