using System.Diagnostics;

namespace EShop.Basket.Application.Telemetry;

public static class BasketActivitySource
{
    public const string SourceName = "EShop.Basket";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
