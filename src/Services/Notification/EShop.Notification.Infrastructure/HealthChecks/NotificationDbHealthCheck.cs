using EShop.Notification.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.Notification.Infrastructure.HealthChecks;

public sealed class NotificationDbHealthCheck : IHealthCheck
{
    private readonly NotificationDbContext _dbContext;

    public NotificationDbHealthCheck(NotificationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy("Notification database is reachable.")
            : HealthCheckResult.Unhealthy("Notification database is unreachable.");
    }
}
