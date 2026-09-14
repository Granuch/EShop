using Microsoft.Extensions.Hosting;

namespace EShop.BuildingBlocks.Infrastructure.Http;

/// <summary>
/// Where the OpenAPI document and the Scalar UI are served: every environment except Production (Ordering
/// audit L10, decision D15). One rule for every service. Before this, Basket, Payment and the gateway served
/// them in Development only, and Catalog, Identity and Ordering everywhere except Testing — so no test
/// could reach them in three services, and Production exposed them in the other three.
/// </summary>
public static class EShopApiDocs
{
    public static bool IsExposedIn(IHostEnvironment environment) => !environment.IsProduction();
}
