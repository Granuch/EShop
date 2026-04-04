using System.Collections.Concurrent;
using System.Text.Json;
using EShop.Basket.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Outbox;

public class BasketRedisOutboxProcessorService : BackgroundService
{
    private static readonly TimeSpan ProcessingLeaseTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BasketRedisOutboxProcessorService> _logger;
    private readonly IBasketMetrics _metrics;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();
    private const int MaxTypeCacheEntries = 1000;
    private const string AllowedNamespacePrefix = "EShop.";

    public BasketRedisOutboxProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<BasketRedisOutboxProcessorService> logger,
        IBasketMetrics metrics)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Basket Redis outbox processor started");
        var nextRecoveryAt = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextRecoveryAt)
                {
                    await RecoverStaleProcessingMessagesAsync(stoppingToken);
                    nextRecoveryAt = DateTime.UtcNow.Add(RecoveryInterval);
                }

                var processed = await ProcessMessageAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in basket outbox processor");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        _logger.LogInformation("Basket Redis outbox processor stopped");
    }

    private async Task<bool> ProcessMessageAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var publishEndpoint = scope.ServiceProvider.GetService<IPublishEndpoint>();

        if (publishEndpoint == null)
        {
            _logger.LogWarning("IPublishEndpoint is not registered. Outbox processor is pausing until messaging is available.");
            await Task.Delay(TimeSpan.FromSeconds(5));
            return false;
        }

        var database = redis.GetDatabase();

        var payload = await database.ListRightPopLeftPushAsync(BasketOutboxKeys.Pending, BasketOutboxKeys.Processing);
        if (payload.IsNullOrEmpty)
        {
            return false;
        }

        RedisOutboxMessage? message = null;

        try
        {
            message = JsonSerializer.Deserialize<RedisOutboxMessage>(payload.ToString(), JsonOptions)
                ?? throw new JsonException("Outbox message deserialization returned null.");

            var leaseKey = GetProcessingLeaseKey(message.Id);
            var leaseAcquired = await database.StringSetAsync(
                leaseKey,
                DateTime.UtcNow.ToString("O"),
                ProcessingLeaseTtl,
                when: When.NotExists);

            if (!leaseAcquired)
            {
                await database.ListRemoveAsync(BasketOutboxKeys.Processing, payload, count: 1);
                await database.ListLeftPushAsync(BasketOutboxKeys.Pending, payload);
                return true;
            }

            var eventType = ResolveType(message.Type);
            if (eventType == null)
            {
                _logger.LogError(
                    "Outbox message {MessageId} has unresolvable or disallowed type '{Type}'. Moving to dead-letter queue",
                    message.Id, message.Type);
                await database.ListRemoveAsync(BasketOutboxKeys.Processing, payload, count: 1);
                var deadPayload = JsonSerializer.Serialize(message with { RetryCount = int.MaxValue }, JsonOptions);
                await database.ListLeftPushAsync(BasketOutboxKeys.DeadLetter, deadPayload);
                _metrics.RecordOutboxRecovery("dead_letter");
                return true;
            }

            var deserialized = JsonSerializer.Deserialize(message.Payload, eventType, JsonOptions)
                ?? throw new JsonException($"Outbox payload for message '{message.Id}' could not be deserialized.");

            if (deserialized is not IIntegrationEvent integrationEvent)
            {
                throw new InvalidOperationException($"Outbox message '{message.Id}' is not an integration event.");
            }

            await publishEndpoint.Publish(deserialized, deserialized.GetType(), context =>
            {
                context.MessageId = message.Id;
                if (Guid.TryParse(message.CorrelationId, out var correlationId))
                {
                    context.CorrelationId = correlationId;
                }
            }, cancellationToken);

            await database.ListRemoveAsync(BasketOutboxKeys.Processing, payload, count: 1);
            await database.KeyDeleteAsync(leaseKey);

            _logger.LogInformation("Published basket outbox message {MessageId} of type {Type}", message.Id, message.Type);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to process basket outbox message. MessageId={MessageId}, Retry={Retry}",
                message?.Id,
                message?.RetryCount ?? -1);

            await database.ListRemoveAsync(BasketOutboxKeys.Processing, payload, count: 1);
            if (message != null)
            {
                await database.KeyDeleteAsync(GetProcessingLeaseKey(message.Id));
            }

            if (message != null)
            {
                var next = message with { RetryCount = message.RetryCount + 1 };
                var nextPayload = JsonSerializer.Serialize(next, JsonOptions);

                if (next.RetryCount >= 5)
                {
                    await database.ListLeftPushAsync(BasketOutboxKeys.DeadLetter, nextPayload);
                    _metrics.RecordOutboxRecovery("dead_letter");
                    _logger.LogError("Basket outbox message {MessageId} moved to dead-letter queue", next.Id);
                }
                else
                {
                    await database.ListLeftPushAsync(BasketOutboxKeys.Pending, nextPayload);
                }
            }

            return true;
        }
    }

    private async Task RecoverStaleProcessingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var database = redis.GetDatabase();

        var payloads = await database.ListRangeAsync(BasketOutboxKeys.Processing, 0, 200);
        if (payloads.Length == 0)
        {
            return;
        }

        var recovered = 0;

        foreach (var payload in payloads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (payload.IsNullOrEmpty)
            {
                continue;
            }

            try
            {
                var message = JsonSerializer.Deserialize<RedisOutboxMessage>(payload.ToString(), JsonOptions);
                if (message == null)
                {
                    continue;
                }

                var leaseExists = await database.KeyExistsAsync(GetProcessingLeaseKey(message.Id));
                if (leaseExists)
                {
                    continue;
                }

                var removed = await database.ListRemoveAsync(BasketOutboxKeys.Processing, payload, count: 1);
                if (removed > 0)
                {
                    await database.ListLeftPushAsync(BasketOutboxKeys.Pending, payload);
                    recovered++;
                    _metrics.RecordOutboxRecovery("recovered");
                }
            }
            catch (JsonException)
            {
                var removed = await database.ListRemoveAsync(BasketOutboxKeys.Processing, payload, count: 1);
                if (removed > 0)
                {
                    await database.ListLeftPushAsync(BasketOutboxKeys.DeadLetter, payload);
                    _metrics.RecordOutboxRecovery("dead_letter");
                }
            }
        }

        if (recovered > 0)
        {
            _logger.LogWarning("Recovered {RecoveredCount} stale outbox messages back to pending queue", recovered);
        }
    }

    private static string GetProcessingLeaseKey(Guid messageId) => $"basket:outbox:lease:{messageId}";

    private static Type? ResolveType(string fullName)
    {
        if (!fullName.StartsWith(AllowedNamespacePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (TypeCache.Count >= MaxTypeCacheEntries && !TypeCache.ContainsKey(fullName))
        {
            return null;
        }

        return TypeCache.GetOrAdd(fullName, static name =>
        {
            var directType = Type.GetType(name);
            if (directType != null)
            {
                return directType;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var resolved = assembly.GetType(name);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        });
    }
}
