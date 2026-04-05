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
            return Task.CompletedTask;
        }

        var configuredKey = _settings.Value.ApiKey;
        var headerName = _settings.Value.HeaderName;

        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(headerName))
        {
            return Task.CompletedTask;
        }

        if (!httpContext.Request.Headers.TryGetValue(headerName, out var providedValues))
        {
            return Task.CompletedTask;
        }

        var providedKey = providedValues.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(providedKey))
        {
            var configuredKeyBytes = Encoding.UTF8.GetBytes(configuredKey);
            var providedKeyBytes = Encoding.UTF8.GetBytes(providedKey);

            if (CryptographicOperations.FixedTimeEquals(configuredKeyBytes, providedKeyBytes))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
