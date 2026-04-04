using System.Text.Json;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using StackExchange.Redis;

namespace EShop.Basket.Infrastructure.Outbox;

public class BasketRedisOutbox : IIntegrationEventOutbox
{
    private readonly IDatabase _database;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public BasketRedisOutbox(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
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
        _database.ListLeftPush(BasketOutboxKeys.Pending, serialized);
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
