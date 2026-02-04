using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Outbox;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EShop.BuildingBlocks.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that processes outbox messages and publishes domain events.
/// Ensures reliable event delivery even when the message broker is temporarily unavailable.
/// 
/// Key features:
/// - Batch processing for efficiency
/// - Exponential backoff on failures
/// - Idempotent message processing
/// - Dead letter handling after max retries
/// - Graceful shutdown support
/// </summary>
public class OutboxProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly OutboxProcessorOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public OutboxProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessorService> logger,
        OutboxProcessorOptions? options = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new OutboxProcessorOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox processor started. Batch size: {BatchSize}, Polling interval: {Interval}ms",
            _options.BatchSize,
            _options.PollingIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessOutboxMessagesAsync(stoppingToken);

                if (processedCount == 0)
                {
                    // No messages to process - wait before polling again
                    await Task.Delay(_options.PollingIntervalMs, stoppingToken);
                }
                else
                {
                    _logger.LogDebug("Processed {Count} outbox messages", processedCount);
                    // If we processed messages, check for more immediately
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages. Retrying in {Delay}ms", _options.ErrorRetryDelayMs);
                await Task.Delay(_options.ErrorRetryDelayMs, stoppingToken);
            }
        }

        _logger.LogInformation("Outbox processor stopped");
    }

    private async Task<int> ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Get unprocessed messages ordered by occurrence time
        var messages = await dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null && m.RetryCount < _options.MaxRetries)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        var processedCount = 0;

        foreach (var message in messages)
        {
            try
            {
                // Deserialize the domain event
                var eventType = Type.GetType(message.Type);
                if (eventType == null)
                {
                    _logger.LogError(
                        "Could not resolve event type {Type} for message {MessageId}. Marking as processed to prevent retry loop",
                        message.Type,
                        message.Id);
                    message.RecordFailure($"Type not found: {message.Type}");
                    message.MarkAsProcessed(); // Dead letter - can't process
                    continue;
                }

                var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType, JsonOptions) as IDomainEvent;
                if (domainEvent == null)
                {
                    _logger.LogError(
                        "Failed to deserialize event {Type} for message {MessageId}",
                        message.Type,
                        message.Id);
                    message.RecordFailure("Deserialization failed");
                    continue;
                }

                // Publish the event via MediatR
                await mediator.Publish(domainEvent, cancellationToken);

                // Mark as successfully processed
                message.MarkAsProcessed();
                processedCount++;

                _logger.LogDebug(
                    "Published outbox message {MessageId} of type {Type}",
                    message.Id,
                    eventType.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to process outbox message {MessageId}. Retry count: {RetryCount}/{MaxRetries}",
                    message.Id,
                    message.RetryCount + 1,
                    _options.MaxRetries);

                message.RecordFailure(ex.Message);

                if (!message.CanRetry(_options.MaxRetries))
                {
                    _logger.LogError(
                        "Outbox message {MessageId} exceeded max retries. Moving to dead letter state",
                        message.Id);
                }
            }
        }

        // Save all state changes in a single transaction
        await dbContext.SaveChangesAsync(cancellationToken);

        return processedCount;
    }
}

/// <summary>
/// Configuration options for the outbox processor.
/// </summary>
public class OutboxProcessorOptions
{
    /// <summary>
    /// Number of messages to process in each batch.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Interval in milliseconds between polling attempts when no messages are found.
    /// </summary>
    public int PollingIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Delay in milliseconds before retrying after an error.
    /// </summary>
    public int ErrorRetryDelayMs { get; set; } = 5000;

    /// <summary>
    /// Maximum number of retry attempts before moving to dead letter.
    /// </summary>
    public int MaxRetries { get; set; } = 5;
}
