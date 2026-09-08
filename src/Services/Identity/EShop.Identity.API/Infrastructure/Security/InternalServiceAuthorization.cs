using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EShop.Identity.API.Infrastructure.Security;

public sealed class InternalServiceRequirement : IAuthorizationRequirement;

public sealed class InternalServiceAuthorizationHandler : AuthorizationHandler<InternalServiceRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<InternalServiceAuthSettings> _settings;

    public InternalServiceAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        IOptions<InternalServiceAuthSettings> settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        InternalServiceRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var configuredKey = _settings.Value.ApiKey;
        var headerName = _settings.Value.HeaderName;

        // API-6. A blank configured key now fails explicitly rather than merely declining to
        // vouch. Both are fail-closed while this is the only handler for the requirement, but
        // context.Fail() cannot be overridden by a second handler, so the guarantee stops
        // depending on there never being one. The tracked appsettings.json ships ApiKey as ""
        // deliberately — see the service CLAUDE.md — so this path is reached routinely, and it
        // must never be mistaken for "no key required".
        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(headerName))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        if (!httpContext.Request.Headers.TryGetValue(headerName, out var providedValues))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var providedKey = providedValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        if (KeysMatch(configuredKey, providedKey))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// API-6. Compares SHA-256 digests rather than the raw bytes.
    ///
    /// <para>
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> is constant-time only across inputs
    /// of the <i>same</i> length — it returns immediately when the lengths differ. Feeding it the
    /// raw keys therefore leaked the configured key's length: an attacker submitting candidates of
    /// varying length could see which length took the slow path and narrow the search space before
    /// guessing a single byte. Hashing first makes both operands exactly 32 bytes, so every
    /// comparison takes the same path regardless of what was submitted.
    /// </para>
    /// </summary>
    private static bool KeysMatch(string configuredKey, string providedKey)
    {
        Span<byte> configuredHash = stackalloc byte[32];
        Span<byte> providedHash = stackalloc byte[32];

        SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey), configuredHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(providedKey), providedHash);

        return CryptographicOperations.FixedTimeEquals(configuredHash, providedHash);
    }
}
