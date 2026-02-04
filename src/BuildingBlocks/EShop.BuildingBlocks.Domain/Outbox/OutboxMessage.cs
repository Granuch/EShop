namespace EShop.BuildingBlocks.Domain.Outbox;

/// <summary>
/// Represents a domain event that has been persisted to the outbox table
/// for reliable delivery. The outbox pattern ensures events are never lost
/// even if the message broker is temporarily unavailable.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>
    /// Unique identifier for idempotency and deduplication.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The full type name of the domain event for deserialization.
    /// </summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>
    /// JSON-serialized event payload.
    /// </summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>
    /// When the event was originally raised.
    /// </summary>
    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>
    /// When the message was processed and published.
    /// Null if not yet processed.
    /// </summary>
    public DateTime? ProcessedOnUtc { get; private set; }

    /// <summary>
    /// Number of processing attempts. Used for retry logic.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Error message from the last failed processing attempt.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// The aggregate type that raised this event.
    /// </summary>
    public string? AggregateType { get; private set; }

    /// <summary>
    /// The aggregate ID that raised this event.
    /// </summary>
    public string? AggregateId { get; private set; }

    private OutboxMessage() { } // For EF Core

    public static OutboxMessage Create(
        IDomainEvent domainEvent,
        string payload,
        string? correlationId = null,
        string? aggregateType = null,
        string? aggregateId = null)
    {
        return new OutboxMessage
        {
            Id = domainEvent.EventId,
            Type = domainEvent.GetType().AssemblyQualifiedName!,
            Payload = payload,
            OccurredOnUtc = domainEvent.OccurredOn,
            ProcessedOnUtc = null,
            RetryCount = 0,
            LastError = null,
            CorrelationId = correlationId,
            AggregateType = aggregateType,
            AggregateId = aggregateId
        };
    }

    /// <summary>
    /// Marks the message as successfully processed.
    /// </summary>
    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTime.UtcNow;
        LastError = null;
    }

    /// <summary>
    /// Records a failed processing attempt.
    /// </summary>
    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error?.Length > 4000 ? error[..4000] : error;
    }

    /// <summary>
    /// Indicates whether this message can be retried.
    /// </summary>
    public bool CanRetry(int maxRetries = 5) => RetryCount < maxRetries && ProcessedOnUtc == null;
}
