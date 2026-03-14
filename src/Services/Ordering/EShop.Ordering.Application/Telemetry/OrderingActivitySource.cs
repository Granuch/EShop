using System.Diagnostics;

namespace EShop.Ordering.Application.Telemetry;

/// <summary>
/// Custom ActivitySource for Ordering service business operations.
/// Source name must match the "additionalSources" parameter passed to AddEShopOpenTelemetry.
/// </summary>
public static class OrderingActivitySource
{
    public const string SourceName = "EShop.Ordering";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
