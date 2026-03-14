using EShop.Ordering.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.Ordering.API.Infrastructure.HealthChecks;

public class OrderingReadinessHealthCheck : IHealthCheck
{
    private readonly OrderingDbContext _dbContext;
    private readonly ILogger<OrderingReadinessHealthCheck> _logger;

    public OrderingReadinessHealthCheck(OrderingDbContext dbContext, ILogger<OrderingReadinessHealthCheck> logger)
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
                return HealthCheckResult.Unhealthy("Cannot connect to ordering database", data: data);
            }

            return HealthCheckResult.Healthy("Ordering service is ready", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ordering readiness health check failed");
            return HealthCheckResult.Unhealthy(
                "Ordering service is not ready",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

public class OrderingLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("Ordering service is alive"));
    }
}
