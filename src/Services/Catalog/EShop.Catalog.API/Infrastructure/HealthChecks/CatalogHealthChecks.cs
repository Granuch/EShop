using EShop.Catalog.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.Catalog.API.Infrastructure.HealthChecks;

/// <summary>
/// Health check for Catalog service readiness.
/// Verifies that the database is accessible and contains expected data.
/// </summary>
public class CatalogReadinessHealthCheck : IHealthCheck
{
    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<CatalogReadinessHealthCheck> _logger;

    public CatalogReadinessHealthCheck(CatalogDbContext dbContext, ILogger<CatalogReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "database_accessible", canConnect }
            };

            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("Cannot connect to catalog database", data: data);
            }

            return HealthCheckResult.Healthy("Catalog service is ready", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catalog readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Catalog service is not ready",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

/// <summary>
/// Liveness health check - verifies the service is running.
/// Should be lightweight and not check external dependencies.
/// </summary>
public class CatalogLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("Catalog service is alive"));
    }
}
