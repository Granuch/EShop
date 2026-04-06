using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using EShop.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.API.Infrastructure.HealthChecks;

/// <summary>
/// Health check for Identity service readiness
/// Verifies that all critical dependencies are operational
/// </summary>
public class IdentityReadinessHealthCheck : IHealthCheck
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<IdentityReadinessHealthCheck> _logger;

    public IdentityReadinessHealthCheck(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ILogger<IdentityReadinessHealthCheck> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if we can query users (database connectivity + Identity setup)
            _ = await _userManager.Users.AnyAsync(cancellationToken);
            
            // Check if required roles exist
            var adminRoleExists = await _roleManager.RoleExistsAsync("Admin");
            var userRoleExists = await _roleManager.RoleExistsAsync("User");

            var data = new Dictionary<string, object>
            {
                { "admin_role_exists", adminRoleExists },
                { "user_role_exists", userRoleExists },
                { "database_accessible", true }
            };

            if (!adminRoleExists || !userRoleExists)
            {
                return HealthCheckResult.Degraded(
                    "Required roles are not configured",
                    data: data);
            }

            return HealthCheckResult.Healthy("Identity service is ready", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Identity readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Identity service is not ready",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

/// <summary>
/// Liveness health check - verifies the service is running
/// Should be lightweight and not check external dependencies
/// </summary>
public class IdentityLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Simple check that the service is alive
        return Task.FromResult(HealthCheckResult.Healthy("Identity service is alive"));
    }
}
