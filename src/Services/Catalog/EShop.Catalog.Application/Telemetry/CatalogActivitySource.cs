using System.Diagnostics;

namespace EShop.Catalog.Application.Telemetry;

/// <summary>
/// Custom ActivitySource for Catalog service business operations.
/// Provides structured spans for key operations like product creation, search, etc.
///
/// Usage:
///   using var activity = CatalogActivitySource.Source.StartActivity("Catalog.CreateProduct");
///   activity?.SetTag("product.id", productId.ToString());
///
/// SECURITY: Never add user PII, tokens, or secrets as tags.
/// </summary>
public static class CatalogActivitySource
{
    /// <summary>
    /// Source name must match the "additionalSources" parameter passed to AddEShopOpenTelemetry.
    /// </summary>
    public const string SourceName = "EShop.Catalog";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
