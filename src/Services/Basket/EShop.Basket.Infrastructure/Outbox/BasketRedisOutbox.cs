using System.Text.Json;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Outbox;

public class BasketRedisOutbox : IIntegrationEventOutbox
{
    private readonly IDatabase _database;
    private readonly ILogger<BasketRedisOutbox> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public BasketRedisOutbox(IConnectionMultiplexer redis, ILogger<BasketRedisOutbox> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public void Enqueue(IIntegrationEvent integrationEvent, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var envelope = new RedisOutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), JsonOptions),
            OccurredOnUtc = integrationEvent.OccurredOn,
            CorrelationId = correlationId,
            RetryCount = 0
        };

        var serialized = JsonSerializer.Serialize(envelope, JsonOptions);
        _ = _database.ListLeftPushAsync(BasketOutboxKeys.Pending, serialized)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception?.GetBaseException(),
                        "Failed to enqueue basket outbox message {MessageId}",
                        envelope.Id);
                }
            }, TaskScheduler.Default);
    }
}

internal sealed record RedisOutboxMessage
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTime OccurredOnUtc { get; init; }
    public string? CorrelationId { get; init; }
    public int RetryCount { get; init; }
}
