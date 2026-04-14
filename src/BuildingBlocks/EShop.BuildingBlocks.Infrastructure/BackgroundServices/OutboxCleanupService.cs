using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that periodically removes old processed outbox messages
/// and processed message tracking records to prevent unbounded table growth.
/// </summary>
public class OutboxCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxCleanupService> _logger;
    private readonly OutboxCleanupOptions _options;

    public OutboxCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxCleanupService> logger,
        OutboxCleanupOptions? options = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new OutboxCleanupOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox cleanup service started. Retention: {RetentionDays} days, Interval: {IntervalHours}h",
            _options.RetentionDays,
            _options.CleanupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(_options.CleanupIntervalHours), stoppingToken);
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during outbox cleanup. Will retry at next interval.");
            }
        }

        _logger.LogInformation("Outbox cleanup service stopped");
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        var isRelational = dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";

        if (isRelational)
        {
            // Use provider-agnostic bulk delete for relational databases
            var outboxDeleted = await dbContext.Set<OutboxMessage>()
                .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var processedDeleted = await dbContext.Set<ProcessedMessage>()
                .Where(m => m.ProcessedOnUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (outboxDeleted > 0 || processedDeleted > 0)
            {
                _logger.LogInformation(
                    "Outbox cleanup completed. Removed {OutboxCount} outbox messages and {ProcessedCount} processed message records older than {CutoffDate}",
                    outboxDeleted,
                    processedDeleted,
                    cutoff);
            }
        }
        else
        {
            // In-memory fallback with bounded batch to prevent OOM
            const int cleanupBatchSize = 1000;

            var oldOutbox = await dbContext.Set<OutboxMessage>()
                .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoff)
                .Take(cleanupBatchSize)
                .ToListAsync(cancellationToken);
            dbContext.Set<OutboxMessage>().RemoveRange(oldOutbox);

            var oldProcessed = await dbContext.Set<ProcessedMessage>()
                .Where(m => m.ProcessedOnUtc < cutoff)
                .Take(cleanupBatchSize)
                .ToListAsync(cancellationToken);
            dbContext.Set<ProcessedMessage>().RemoveRange(oldProcessed);

            await dbContext.SaveChangesAsync(cancellationToken);

            if (oldOutbox.Count > 0 || oldProcessed.Count > 0)
            {
                _logger.LogInformation(
                    "Outbox cleanup completed. Removed {OutboxCount} outbox messages and {ProcessedCount} processed message records",
                    oldOutbox.Count,
                    oldProcessed.Count);
            }
        }
    }
}

/// <summary>
/// Configuration options for the outbox cleanup service.
/// </summary>
public class OutboxCleanupOptions
{
    /// <summary>
    /// Number of days to retain processed messages before cleanup.
    /// </summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// Interval in hours between cleanup runs.
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 6;
}
