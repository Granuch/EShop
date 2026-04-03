using System.Collections.Concurrent;
using System.Text.Json;
using EShop.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Outbox;

public class BasketRedisOutboxProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BasketRedisOutboxProcessorService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

    public BasketRedisOutboxProcessorService(
        IServiceScopeFactory scopeFactory,
        ILogger<BasketRedisOutboxProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Basket Redis outbox processor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

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

            var eventType = ResolveType(message.Type);
            if (eventType == null)
            {
                throw new TypeLoadException($"Outbox event type '{message.Type}' could not be resolved.");
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
                var next = message with { RetryCount = message.RetryCount + 1 };
                var nextPayload = JsonSerializer.Serialize(next, JsonOptions);

                if (next.RetryCount >= 5)
                {
                    await database.ListLeftPushAsync(BasketOutboxKeys.DeadLetter, nextPayload);
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

    private static Type? ResolveType(string fullName)
    {
        return TypeCache.GetOrAdd(fullName, typeName =>
        {
            var directType = Type.GetType(typeName);
            if (directType != null)
            {
                return directType;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var resolved = assembly.GetType(typeName);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return null;
        });
    }
}
