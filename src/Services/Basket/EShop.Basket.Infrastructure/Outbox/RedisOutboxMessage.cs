using System.Text.Json;
using EShop.BuildingBlocks.Messaging;

namespace EShop.Basket.Infrastructure.Outbox;

/// <summary>
/// One entry of Basket's Redis outbox (<see cref="BasketOutboxKeys.Pending"/>), as
/// <see cref="BasketRedisOutboxProcessorService"/> reads it. Its <see cref="Id"/> is the event's <c>EventId</c>, which the
/// processor publishes as the MassTransit <c>MessageId</c> — the id Ordering's consumer deduplicates on.
/// </summary>
internal sealed record RedisOutboxMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTime OccurredOnUtc { get; init; }
    public string? CorrelationId { get; init; }
    public int RetryCount { get; init; }

    /// <summary>The serialized envelope for <paramref name="integrationEvent"/>, ready to push onto the outbox.</summary>
    public static string Serialize(IIntegrationEvent integrationEvent, string? correlationId)
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

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }
}
