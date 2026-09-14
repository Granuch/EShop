using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Notification.Infrastructure.BackgroundServices;

/// <summary>
/// Notification audit S5 (M8, D8). <c>NotificationLogs</c> keeps each recipient's address and user id, and nothing
/// removed a row. Every <see cref="NotificationLogRetentionSettings.CleanupIntervalHours"/> this deletes the rows whose
/// last update is older than <see cref="NotificationLogRetentionSettings.RetentionDays"/> (90 by default), whatever their
/// status.
/// <para>The row is also the duplicate-delivery claim (D5), which only matters while a copy of the event can still
/// arrive: minutes of redelivery, at most the outbox's 7 days. Ninety days is far past that.</para>
/// </summary>
public sealed class NotificationLogRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationLogRetentionSettings> settings,
    TimeProvider timeProvider,
    ILogger<NotificationLogRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, settings.Value.CleanupIntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var deleted = await DeleteExpiredAsync(
                    dbContext, settings.Value, timeProvider.GetUtcNow().UtcDateTime, stoppingToken);

                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Deleted {Count} notification log rows older than {RetentionDays} days.",
                        deleted, settings.Value.RetentionDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification log retention failed. It runs again at the next interval.");
            }
        }
    }

    /// <summary>
    /// Deletes the expired rows in batches and returns how many. Does nothing on EF InMemory (the Testing host), which
    /// cannot run a bulk delete.
    /// </summary>
    public static async Task<int> DeleteExpiredAsync(
        NotificationDbContext dbContext,
        NotificationLogRetentionSettings settings,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return 0;
        }

        var cutoff = now.AddDays(-Math.Max(1, settings.RetentionDays));
        var batchSize = Math.Max(1, settings.BatchSize);
        var total = 0;

        while (true)
        {
            var deleted = await dbContext.NotificationLogs
                .Where(log => (log.UpdatedAt ?? log.CreatedAt) < cutoff)
                .OrderBy(log => log.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
            if (deleted < batchSize)
            {
                return total;
            }
        }
    }
}
