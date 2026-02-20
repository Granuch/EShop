namespace EShop.BuildingBlocks.Domain.Outbox;

/// <summary>
/// Explicit status for outbox messages.
/// Provides clear queryability and distinguishes between successfully processed
/// and dead-lettered messages (which previously both had ProcessedOnUtc != null).
/// </summary>
public enum OutboxMessageStatus
{
    /// <summary>
    /// Message is waiting to be processed and published.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Message was successfully processed and published.
    /// </summary>
    Processed = 1,

    /// <summary>
    /// Message permanently failed after max retries or non-transient error.
    /// Requires manual investigation.
    /// </summary>
    DeadLettered = 2
}
