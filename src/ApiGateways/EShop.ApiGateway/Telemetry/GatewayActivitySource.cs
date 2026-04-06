using System.Diagnostics;

namespace EShop.ApiGateway.Telemetry;

public static class GatewayActivitySource
{
    public const string SourceName = "EShop.ApiGateway";

    public static readonly ActivitySource Instance = new(SourceName);
}
