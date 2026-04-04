using EShop.Basket.Infrastructure.Outbox;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace EShop.Basket.API.Infrastructure.HealthChecks;

public class BasketReadinessHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public BasketReadinessHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _redis.GetDatabase();
            var pong = await database.PingAsync();

            return HealthCheckResult.Healthy("Basket service is ready", new Dictionary<string, object>
            {
                ["redis_latency_ms"] = pong.TotalMilliseconds
            });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Basket service is not ready", ex);
        }
    }
}

public class BasketOutboxHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public BasketOutboxHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _redis.GetDatabase();

            var pending = await database.ListLengthAsync(BasketOutboxKeys.Pending);
            var processing = await database.ListLengthAsync(BasketOutboxKeys.Processing);
            var deadLetter = await database.ListLengthAsync(BasketOutboxKeys.DeadLetter);

            var data = new Dictionary<string, object>
            {
                ["outbox_pending_count"] = pending,
                ["outbox_processing_count"] = processing,
                ["outbox_dead_letter_count"] = deadLetter
            };

            if (deadLetter > 0)
            {
                return HealthCheckResult.Degraded("Basket outbox has dead-lettered messages", data: data);
            }

            return HealthCheckResult.Healthy("Basket outbox is healthy", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Basket outbox health check failed", ex);
        }
    }
}

public class BasketLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("Basket service is alive"));
    }
}
