using Microsoft.Extensions.Hosting;

namespace EShop.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// Validates the JWT signing key a service host is about to trust (Ordering audit Stage 22, decision D18).
///
/// <para>
/// <b>Why it is shared.</b> Identity, Catalog, Ordering and Payment each carried their own copy of this check,
/// and the copies drifted: Catalog's and Ordering's placeholder lists once had five patterns against
/// Identity's seven, missing <c>LOCAL_</c> and <c>REPLACE_WITH_</c> — so the exact placeholder that stops
/// Identity booted those two cleanly on the same shared key. Ordering calls this; the other three still
/// have their own copies until they are moved over.
/// </para>
///
/// <para>
/// <b>The rule.</b> A missing key, or one shorter than <see cref="MinimumLength"/> characters, is refused in
/// every environment: HS256 needs 256 bits. A key containing a placeholder pattern is refused outside
/// Development and Testing — Sandbox included, as <see cref="CorsOriginGuard"/> does, because Sandbox is
/// deployed and reachable. Call it while composing the host, so a bad deploy never starts.
/// </para>
/// </summary>
public static class JwtSecretGuard
{
    /// <summary>256 bits for HS256.</summary>
    public const int MinimumLength = 32;

    /// <summary>
    /// Substrings that mark a key as a not-yet-replaced placeholder, matched case-insensitively. The same
    /// seven Identity's guard uses; each appears in this repo's tracked configuration or templates.
    /// </summary>
    public static IReadOnlyList<string> PlaceholderPatterns { get; } =
        ["#{", "CHANGE_ME", "LOCAL_", "REPLACE_WITH_", "YOUR_", "TestKey", "placeholder"];

    /// <summary>Returns <paramref name="secretKey"/> if a host may sign and validate tokens with it; throws otherwise.</summary>
    /// <exception cref="InvalidOperationException">The key is missing, too short, or a placeholder where one is not allowed.</exception>
    public static string Validate(string? secretKey, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "JWT SecretKey is not configured. Set JwtSettings:SecretKey in configuration or environment variables.");
        }

        if (secretKey.Length < MinimumLength)
        {
            throw new InvalidOperationException(
                $"JWT SecretKey must be at least {MinimumLength} characters (256 bits) for HS256. Current length: {secretKey.Length}.");
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return secretKey;
        }

        foreach (var pattern in PlaceholderPatterns)
        {
            if (secretKey.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"JWT SecretKey contains placeholder pattern '{pattern}'. Replace with a secure secret before deploying to {environment.EnvironmentName}.");
            }
        }

        return secretKey;
    }
}
