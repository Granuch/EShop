using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace EShop.BuildingBlocks.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that processes outbox messages and publishes events.
/// 
/// Routing logic:
/// - If the deserialized type implements <see cref="IIntegrationEvent"/>, publish via MassTransit (cross-service).
/// - If the deserialized type implements <see cref="IDomainEvent"/>, publish via MediatR (in-process).
/// 
/// Key features:
/// - Batch processing with row-level locking (FOR UPDATE SKIP LOCKED)
/// - Transient vs non-transient error discrimination
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

    /// <summary>
    /// Non-transient exception types that should immediately dead-letter the message.
    /// Note: InvalidOperationException is intentionally excluded — EF Core throws it
    /// for transient errors (concurrency, connection pool, transaction isolation).
    /// </summary>
    private static readonly HashSet<Type> NonTransientExceptions =
    [
        typeof(JsonException),
        typeof(TypeLoadException),
        typeof(ArgumentException),
        typeof(FormatException),
        typeof(NotSupportedException),
    ];

    /// <summary>
    /// Cache of resolved types to avoid scanning all assemblies on every message.
    /// Bounded to prevent unbounded growth from malformed/poisoned type names.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();
    private const int MaxTypeCacheEntries = 1000;
    private const string AllowedNamespacePrefix = "EShop.";

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
        // R13: Validate that DbContext base type is registered at startup
        using (var validationScope = _scopeFactory.CreateScope())
        {
            var dbContext = validationScope.ServiceProvider.GetService<DbContext>();
            if (dbContext == null)
            {
                _logger.LogCritical(
                    "DbContext base type is not registered in DI. OutboxProcessorService cannot function. " +
                    "Add: services.AddScoped<DbContext>(sp => sp.GetRequiredService<YourDbContext>())");
                throw new InvalidOperationException(
                    "DbContext base type is not registered in DI. OutboxProcessorService cannot function.");
            }
        }

        _logger.LogInformation(
            "Outbox processor started. Batch size: {BatchSize}, Polling interval: {Interval}ms",
            _options.BatchSize,
            _options.PollingIntervalMs);

        var consecutiveEmptyPolls = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessOutboxMessagesAsync(stoppingToken);

                if (processedCount == 0)
                {
                    consecutiveEmptyPolls++;
                    // Exponential backoff: 1s, 2s, 4s, ... up to 30s on consecutive empty polls
                    var delay = Math.Min(
                        _options.PollingIntervalMs * (1 << Math.Min(consecutiveEmptyPolls - 1, 4)),
                        _options.MaxPollingIntervalMs);
                    await Task.Delay(delay, stoppingToken);
                }
                else
                {
                    consecutiveEmptyPolls = 0;
                    _logger.LogDebug("Processed {Count} outbox messages", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
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
        var bus = scope.ServiceProvider.GetService<IBus>();

        // Use raw SQL with FOR UPDATE SKIP LOCKED to prevent multiple instances
        // from processing the same messages concurrently.
        // Falls back to LINQ for in-memory provider (testing).
        List<OutboxMessage> messages;
        var isRelational = dbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";

        if (isRelational)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                messages = await dbContext.Set<OutboxMessage>()
                    .FromSqlRaw(
                        """
                        SELECT * FROM outbox_messages
                        WHERE "ProcessedOnUtc" IS NULL AND "RetryCount" < {0}
                        ORDER BY "OccurredOnUtc"
                        LIMIT {1}
                        FOR UPDATE SKIP LOCKED
                        """,
                        _options.MaxRetries,
                        _options.BatchSize)
                    .ToListAsync(cancellationToken);

                if (messages.Count == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return 0;
                }

                var processedCount = await ProcessBatchAsync(messages, mediator, bus, cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return processedCount;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        else
        {
            // In-memory fallback for testing — no row locking needed
            messages = await dbContext.Set<OutboxMessage>()
                .Where(m => m.ProcessedOnUtc == null && m.RetryCount < _options.MaxRetries)
                .OrderBy(m => m.OccurredOnUtc)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken);

            if (messages.Count == 0)
            {
                return 0;
            }

            var processedCount = await ProcessBatchAsync(messages, mediator, bus, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return processedCount;
        }
    }

    private async Task<int> ProcessBatchAsync(
        List<OutboxMessage> messages,
        IMediator mediator,
        IBus? bus,
        CancellationToken cancellationToken)
    {
        var processedCount = 0;

        foreach (var message in messages)
        {
            try
            {
                var eventType = ResolveType(message.Type);
                if (eventType == null)
                {
                    _logger.LogError(
                        "Could not resolve event type {Type} for message {MessageId}. Dead-lettering",
                        message.Type,
                        message.Id);
                    message.MarkAsDeadLettered($"Type not found: {message.Type}");
                    continue;
                }

                var deserialized = JsonSerializer.Deserialize(message.Payload, eventType, JsonOptions);
                if (deserialized == null)
                {
                    _logger.LogError(
                        "Failed to deserialize event {Type} for message {MessageId}. Dead-lettering",
                        message.Type,
                        message.Id);
                    message.MarkAsDeadLettered("Deserialization returned null");
                    continue;
                }

                if (deserialized is IIntegrationEvent)
                {
                    if (bus == null)
                    {
                        _logger.LogWarning(
                            "MassTransit IBus not available. Cannot publish integration event {Type} for message {MessageId}. Will retry.",
                            eventType.Name,
                            message.Id);
                        message.RecordFailure("MassTransit IBus not available");
                        continue;
                    }

                    await bus.Publish(deserialized, deserialized.GetType(), context =>
                    {
                        context.MessageId = message.Id;
                        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
                        {
                            context.CorrelationId = Guid.TryParse(message.CorrelationId, out var cid)
                                ? cid
                                : Guid.NewGuid();
                        }
                    }, cancellationToken);

                    _logger.LogInformation(
                        "Published integration event {MessageId} of type {Type} via MassTransit",
                        message.Id,
                        eventType.Name);
                }
                else if (deserialized is IDomainEvent domainEvent)
                {
                    await mediator.Publish(domainEvent, cancellationToken);

                    _logger.LogDebug(
                        "Published domain event {MessageId} of type {Type} via MediatR",
                        message.Id,
                        eventType.Name);
                }
                else
                {
                    _logger.LogWarning(
                        "Outbox message {MessageId} of type {Type} is neither IDomainEvent nor IIntegrationEvent. Dead-lettering.",
                        message.Id,
                        eventType.Name);
                    message.MarkAsDeadLettered($"Unknown event category: {eventType.Name}");
                    continue;
                }

                message.MarkAsProcessed();
                processedCount++;
            }
            catch (Exception ex) when (IsNonTransient(ex))
            {
                _logger.LogError(ex,
                    "Non-transient error processing outbox message {MessageId}. Dead-lettering",
                    message.Id);
                message.MarkAsDeadLettered(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Transient error processing outbox message {MessageId}. Retry count: {RetryCount}/{MaxRetries}",
                    message.Id,
                    message.RetryCount + 1,
                    _options.MaxRetries);

                message.RecordFailure(ex.Message);

                if (!message.CanRetry(_options.MaxRetries))
                {
                    _logger.LogError(
                        "Outbox message {MessageId} exceeded max retries ({MaxRetries}). Dead-lettering",
                        message.Id,
                        _options.MaxRetries);
                    message.MarkAsDeadLettered(ex.Message);
                }
            }
        }

        return processedCount;
    }

    /// <summary>
    /// Resolves a type by FullName with caching.
    /// Uses a ConcurrentDictionary to avoid scanning all loaded assemblies on every message.
    /// Validates namespace prefix and enforces bounded cache size.
    /// </summary>
    private static Type? ResolveType(string fullName)
    {
        // Validate namespace prefix to prevent cache pollution from malformed type names
        if (!fullName.StartsWith(AllowedNamespacePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // Enforce bounded cache size
        if (TypeCache.Count >= MaxTypeCacheEntries && !TypeCache.ContainsKey(fullName))
        {
            return null;
        }

        return TypeCache.GetOrAdd(fullName, static name =>
        {
            // Try direct resolution first (works if assembly is already loaded)
            var type = Type.GetType(name);
            if (type != null)
                return type;

            // Scan loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(name);
                if (type != null)
                    return type;
            }

            return null;
        });
    }

    /// <summary>
    /// Determines whether an exception is non-transient (will never succeed on retry).
    /// </summary>
    private static bool IsNonTransient(Exception ex)
    {
        var exceptionType = ex is AggregateException agg
            ? agg.InnerException?.GetType() ?? ex.GetType()
            : ex.GetType();

        return NonTransientExceptions.Contains(exceptionType);
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
    /// Maximum polling interval in milliseconds when using exponential backoff on empty polls.
    /// </summary>
    public int MaxPollingIntervalMs { get; set; } = 30_000;

    /// <summary>
    /// Delay in milliseconds before retrying after an error.
    /// </summary>
    public int ErrorRetryDelayMs { get; set; } = 5000;

    /// <summary>
    /// Maximum number of retry attempts before moving to dead letter.
    /// </summary>
    public int MaxRetries { get; set; } = 5;
}
