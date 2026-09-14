using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EShop.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// Reads and validates <c>Cors:AllowedOrigins</c> for a service host.
///
/// Two things this exists for, both of which bit the repo:
///
/// 1. <b>Placeholder origins passed the old check.</b> The tracked
///    <c>appsettings.Production.json</c> of several services ships
///    <c>["https://your-production-frontend.com"]</c>, which is a non-empty array and so
///    satisfied a bare "is it empty?" guard — a deploy that never configured CORS looked
///    configured. The guard now rejects known placeholder shapes as well as an empty list.
///
/// 2. <b>The old check ran too late to be a startup guard.</b> It lived inside the
///    <c>AddPolicy</c> lambda, which CORS builds lazily on first use, so a misconfigured
///    deploy started healthy and then threw a 500 on the first cross-origin request. Call
///    this while composing the host and pass the result into the policy.
/// </summary>
public static class CorsOriginGuard
{
    /// <summary>
    /// Substrings that mark a value as a not-yet-replaced placeholder. Matched
    /// case-insensitively. Kept deliberately narrow — each entry either appears in this
    /// repo's tracked configuration or, like example.com (RFC 2606), can never be a real
    /// production origin.
    /// </summary>
    private static readonly string[] PlaceholderPatterns =
    [
        "#{",
        "CHANGE_ME",
        "REPLACE_WITH_",
        "your-production-frontend",
        "example.com",
    ];

    /// <summary>
    /// Returns the configured allowed origins, throwing if they are unusable in an
    /// environment that requires real ones. Development and Testing are exempt — both
    /// legitimately run with no configured origin.
    /// </summary>
    public static string[] GetValidatedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return origins;
        }

        if (origins.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cors:AllowedOrigins is empty in {environment.EnvironmentName}. " +
                "Configure allowed origins before deploying to non-development environments.");
        }

        foreach (var origin in origins)
        {
            foreach (var pattern in PlaceholderPatterns)
            {
                if (origin.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cors:AllowedOrigins contains the placeholder origin '{origin}' in " +
                        $"{environment.EnvironmentName} (matched '{pattern}'). Replace it with the real " +
                        "front-end origin before deploying.");
                }
            }
        }

        return origins;
    }
}
