using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Notifications;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Health;

public sealed class EmailQueueHealthCheck : IHealthCheck
{
    private readonly GatewayEmailQueue _queue;
    private readonly EmailQueueHealthOptions _options;

    public EmailQueueHealthCheck(
        GatewayEmailQueue queue,
        IOptions<EmailQueueHealthOptions> options)
    {
        _queue = queue;
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = _queue.GetSnapshot();

        var data = new Dictionary<string, object>
        {
            ["capacity"] = snapshot.Capacity,
            ["enqueued"] = snapshot.EnqueuedCount,
            ["dequeued"] = snapshot.DequeuedCount,
            ["dropped"] = snapshot.DroppedCount,
            ["backlog"] = snapshot.BacklogCount
        };

        if (snapshot.BacklogCount >= _options.BacklogUnhealthyThreshold
            || snapshot.DroppedCount >= _options.DroppedUnhealthyThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Email queue is unhealthy.", data: data));
        }

        if (snapshot.BacklogCount >= _options.BacklogWarningThreshold
            || snapshot.DroppedCount >= _options.DroppedWarningThreshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Email queue is degraded.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Email queue is healthy.", data: data));
    }
}
