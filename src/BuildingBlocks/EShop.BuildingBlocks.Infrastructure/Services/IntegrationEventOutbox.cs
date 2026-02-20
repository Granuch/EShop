using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EShop.BuildingBlocks.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation that persists integration events to the outbox table.
/// Events are enqueued into the same DbContext transaction, ensuring atomicity
/// with domain state changes.
/// </summary>
public class IntegrationEventOutbox : IIntegrationEventOutbox
{
    private readonly DbContext _dbContext;

    /// <summary>
    /// Maximum allowed payload size in bytes. Messages exceeding this are rejected
    /// at enqueue time to prevent oversized messages from entering the outbox.
    /// Default: 256 KB.
    /// </summary>
    private const int MaxPayloadSizeBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public IntegrationEventOutbox(DbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public void Enqueue(IIntegrationEvent integrationEvent, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), JsonOptions);

        if (payload.Length > MaxPayloadSizeBytes)
        {
            throw new ArgumentException(
                $"Integration event payload exceeds maximum size. " +
                $"Type={integrationEvent.GetType().Name}, Size={payload.Length} bytes, Max={MaxPayloadSizeBytes} bytes. " +
                $"Consider splitting large payloads or using a reference-based approach.");
        }

        var outboxMessage = OutboxMessage.CreateForIntegration(
            integrationEvent.EventId,
            integrationEvent.GetType(),
            payload,
            integrationEvent.OccurredOn,
            correlationId);

        _dbContext.Set<OutboxMessage>().Add(outboxMessage);
    }
}
