using EShop.BuildingBlocks.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace EShop.BuildingBlocks.Infrastructure.Services;

/// <summary>
/// HTTP-aware implementation of ICurrentUserContext.
/// Resolves user identity from HttpContext when available.
/// Falls back to system context for background operations.
/// Thread-safe via HttpContextAccessor's async-local storage.
/// </summary>
public sealed class HttpCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _correlationId;

    public HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

        // Generate correlation ID once per scope
        // Try to get from request header first (for distributed tracing)
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationHeader) == true
            && !string.IsNullOrWhiteSpace(correlationHeader))
        {
            _correlationId = correlationHeader.ToString();
        }
        else
        {
            _correlationId = Guid.NewGuid().ToString("N");
        }
    }

    public string? UserId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                // Background service context - no HTTP request
                return "system";
            }

            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // Try standard claim types in order of preference
            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("sub")
                ?? user.FindFirstValue("id");
        }
    }

    public string? UserName
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return "System";
            }

            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return user.FindFirstValue(ClaimTypes.Name)
                ?? user.FindFirstValue(ClaimTypes.Email)
                ?? user.FindFirstValue("preferred_username");
        }
    }

    public bool IsAuthenticated
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            return httpContext?.User?.Identity?.IsAuthenticated == true;
        }
    }

    public string CorrelationId => _correlationId;
}
