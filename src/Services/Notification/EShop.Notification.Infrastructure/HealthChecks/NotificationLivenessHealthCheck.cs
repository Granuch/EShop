using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.Notification.Infrastructure.HealthChecks;

/// <summary>
/// Notification registered only <c>ready</c>-tagged checks, so its <c>/health/live</c> fell back
/// to <c>check.Tags.Contains("live") || check.Tags.Count == 0</c> — a predicate that matched
/// nothing and therefore always reported Healthy. Liveness now has a real check behind it,
/// matching the pair every other component has.
///
/// It touches no external dependency on purpose: a liveness probe that fails on an SMTP or
/// database blip restarts a pod that would otherwise have recovered.
/// </summary>
public sealed class NotificationLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("Notification service is alive"));
    }
}
