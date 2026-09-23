using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.BuildingBlocks.Infrastructure.SystemAdmin;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.API.Endpoints;

/// <summary>
/// Ordering's slice of the System page's read-only settings (admin panel S19, endpoint #85, decision Q9c). The gateway
/// serves <c>GET /api/v1/admin/settings</c> itself and asks this endpoint, with the caller's token, for how an order is
/// priced; the gateway does not route the path here, so a client never reaches it directly.
/// </summary>
public static class SystemSettingsEndpoints
{
    public static IEndpointRouteBuilder MapSystemSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // TaxApplied and ShippingCharged are literals because nothing in Ordering could make them anything else: an
        // order's total is the sum of its lines (Order.RecalculateTotal). OrderingSettingsTests creates an order and
        // checks exactly that, so the day a shipping line or a tax is added to the total, that test goes red and this
        // answer has to change with it.
        app.MapGet(SystemAdminPaths.Settings, () =>
                Results.Ok(new PricingSettingsDto(Order.PricingCurrency, TaxApplied: false, ShippingCharged: false)))
            .RequireAuthorization(EShopPermissions.SystemManage)
            .WithName("GetPricingSettings")
            .WithTags("Admin — System")
            .Produces<PricingSettingsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
