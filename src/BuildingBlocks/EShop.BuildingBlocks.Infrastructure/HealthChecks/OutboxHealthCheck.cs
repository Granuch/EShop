using EShop.BuildingBlocks.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.Infrastructure.HealthChecks;

/// <summary>
/// Health check that monitors outbox message depth and dead-lettered messages.
/// Reports degraded/unhealthy when dead-lettered messages accumulate (indicating
/// processing failures that need manual intervention) or when pending messages
/// grow unboundedly (indicating the outbox processor is falling behind).
/// </summary>
public class OutboxHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxHealthCheck> _logger;
    private readonly OutboxHealthCheckOptions _options;

    public OutboxHealthCheck(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxHealthCheck> logger,
        OutboxHealthCheckOptions? options = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options ?? new OutboxHealthCheckOptions();
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetService<DbContext>();

            if (dbContext == null)
            {
                return HealthCheckResult.Degraded(
                    "DbContext not registered — cannot monitor outbox",
                    data: new Dictionary<string, object> { ["db_context_registered"] = false });
            }

            var deadLetteredCount = await dbContext.Set<OutboxMessage>()
                .CountAsync(m => m.Status == OutboxMessageStatus.DeadLettered, cancellationToken);

            var pendingCount = await dbContext.Set<OutboxMessage>()
                .CountAsync(m => m.Status == OutboxMessageStatus.Pending, cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["dead_lettered"] = deadLetteredCount,
                ["pending"] = pendingCount,
                ["dead_letter_threshold"] = _options.DeadLetterWarningThreshold,
                ["pending_threshold"] = _options.PendingWarningThreshold
            };

            if (deadLetteredCount > _options.DeadLetterWarningThreshold)
            {
                _logger.LogWarning(
                    "Outbox has {DeadLetteredCount} dead-lettered messages (threshold: {Threshold}). Manual investigation required.",
                    deadLetteredCount, _options.DeadLetterWarningThreshold);

                return HealthCheckResult.Unhealthy(
                    $"Outbox has {deadLetteredCount} dead-lettered messages requiring investigation",
                    data: data);
            }

            if (pendingCount > _options.PendingWarningThreshold)
            {
                _logger.LogWarning(
                    "Outbox has {PendingCount} pending messages (threshold: {Threshold}). Processor may be falling behind.",
                    pendingCount, _options.PendingWarningThreshold);

                return HealthCheckResult.Degraded(
                    $"Outbox has {pendingCount} pending messages — processor may be falling behind",
                    data: data);
            }

            return HealthCheckResult.Healthy("Outbox is healthy", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox health check failed");
            return HealthCheckResult.Unhealthy("Outbox health check failed", ex);
        }
    }
}

/// <summary>
/// Configuration options for the outbox health check.
/// </summary>
public class OutboxHealthCheckOptions
{
    /// <summary>
    /// Number of dead-lettered messages before reporting unhealthy.
    /// </summary>
    public int DeadLetterWarningThreshold { get; set; } = 10;

    /// <summary>
    /// Number of pending messages before reporting degraded.
    /// </summary>
    public int PendingWarningThreshold { get; set; } = 100;
}
