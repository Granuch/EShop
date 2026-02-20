using System.Diagnostics;

namespace EShop.Identity.Application.Telemetry;

/// <summary>
/// Custom ActivitySource for Identity service business operations.
/// Provides structured spans for key operations like login, registration, etc.
///
/// Usage:
///   using var activity = IdentityActivitySource.Source.StartActivity("Identity.Login");
///   activity?.SetTag("user.id", userId);
///
/// SECURITY: Never add passwords, tokens, secrets, or raw JWTs as tags.
/// Only use opaque identifiers (userId, hashedEmail) for structured tagging.
/// </summary>
public static class IdentityActivitySource
{
    /// <summary>
    /// Source name must match the "additionalSources" parameter passed to AddEShopOpenTelemetry.
    /// </summary>
    public const string SourceName = "EShop.Identity";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
