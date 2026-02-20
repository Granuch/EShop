using EShop.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EShop.BuildingBlocks.Infrastructure.Consumers;

/// <summary>
/// Base class for idempotent MassTransit consumers.
/// Uses the processed_messages table with INSERT-first strategy to prevent
/// duplicate handling in at-least-once delivery scenarios.
/// 
/// For relational databases (PostgreSQL):
/// - Uses ON CONFLICT DO NOTHING to avoid aborting the transaction on duplicates.
/// - Wraps claim INSERT + HandleAsync in a single transaction.
/// - On failure, transaction rollback atomically removes the claim, allowing MassTransit retry.
/// 
/// For in-memory databases (testing):
/// - Falls back to EF-based claim with best-effort removal on failure.
/// </summary>
/// <typeparam name="TMessage">The integration event type being consumed.</typeparam>
/// <typeparam name="TDbContext">The DbContext type for accessing the processed message store.</typeparam>
public abstract class IdempotentConsumer<TMessage, TDbContext> : IConsumer<TMessage>
    where TMessage : class
    where TDbContext : DbContext
{
    protected readonly TDbContext DbContext;
    protected readonly ILogger Logger;

    protected IdempotentConsumer(TDbContext dbContext, ILogger logger)
    {
        DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Minimum schema version this consumer supports.
    /// Override in derived consumers to reject messages with outdated schemas.
    /// Default: 1 (accepts all versions).
    /// </summary>
    protected virtual int MinSupportedVersion => 1;

    public async Task Consume(ConsumeContext<TMessage> context)
    {
        if (context.MessageId is null)
        {
            Logger.LogError(
                "Message of type {MessageType} received without MessageId. Rejecting to prevent idempotency bypass.",
                typeof(TMessage).Name);
            throw new ArgumentException(
                $"Message of type {typeof(TMessage).Name} has no MessageId. Cannot guarantee idempotency.");
        }

        // R5: Schema version checking for safe independent deployment
        if (context.Message is IntegrationEvent integrationEvent && integrationEvent.Version < MinSupportedVersion)
        {
            Logger.LogWarning(
                "Message version {Version} is below minimum supported version {MinVersion} for {MessageType}. Skipping.",
                integrationEvent.Version, MinSupportedVersion, typeof(TMessage).Name);
            return;
        }

        var messageId = context.MessageId.Value;
        var correlationId = context.CorrelationId?.ToString();
        var messageType = typeof(TMessage).Name;

        using (Logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = messageId,
            ["CorrelationId"] = correlationId,
            ["MessageType"] = messageType
        }))
        {
            var isRelational = DbContext.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";

            if (isRelational)
            {
                await ConsumeWithTransactionAsync(context, messageId, messageType);
            }
            else
            {
                await ConsumeInMemoryAsync(context, messageId, messageType);
            }
        }
    }

    /// <summary>
    /// Implement the actual message handling logic in derived consumers.
    /// </summary>
    protected abstract Task HandleAsync(ConsumeContext<TMessage> context, CancellationToken cancellationToken);

    /// <summary>
    /// Relational database path: wraps claim + handling in a single transaction.
    /// Uses ON CONFLICT DO NOTHING so a duplicate INSERT does not abort the PostgreSQL transaction.
    /// On failure, the transaction rolls back and the claim is atomically removed.
    /// </summary>
    private async Task ConsumeWithTransactionAsync(
        ConsumeContext<TMessage> context,
        Guid messageId,
        string messageType)
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync(context.CancellationToken);

        try
        {
            // Use ON CONFLICT DO NOTHING to avoid aborting the PostgreSQL transaction
            // on a unique constraint violation. Returns 1 if inserted, 0 if duplicate.
            var claimed = await DbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO processed_messages ("MessageId", "MessageType", "ProcessedOnUtc")
                VALUES ({0}, {1}, {2})
                ON CONFLICT ("MessageId") DO NOTHING
                """,
                messageId, messageType, DateTime.UtcNow);

            if (claimed == 0)
            {
                Logger.LogWarning(
                    "Duplicate message detected. MessageId={MessageId}, Type={MessageType}. Skipping.",
                    messageId, messageType);
                await transaction.RollbackAsync(context.CancellationToken);
                return;
            }

            Logger.LogInformation(
                "Consuming message. MessageId={MessageId}, Type={MessageType}",
                messageId, messageType);

            await HandleAsync(context, context.CancellationToken);

            await transaction.CommitAsync(context.CancellationToken);

            Logger.LogInformation(
                "Message consumed successfully. MessageId={MessageId}, Type={MessageType}",
                messageId, messageType);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Error consuming message. MessageId={MessageId}, Type={MessageType}. " +
                "Transaction rolled back — claim removed atomically for retry.",
                messageId, messageType);

            await transaction.RollbackAsync(context.CancellationToken);
            throw; // Let MassTransit retry policy handle it
        }
    }

    /// <summary>
    /// In-memory database path (testing): EF-based claim with best-effort removal on failure.
    /// In-memory provider does not support transactions or raw SQL.
    /// </summary>
    private async Task ConsumeInMemoryAsync(
        ConsumeContext<TMessage> context,
        Guid messageId,
        string messageType)
    {
        if (!await TryClaimMessageInMemoryAsync(messageId, messageType, context.CancellationToken))
        {
            Logger.LogWarning(
                "Duplicate message detected. MessageId={MessageId}, Type={MessageType}. Skipping.",
                messageId, messageType);
            return;
        }

        Logger.LogInformation(
            "Consuming message. MessageId={MessageId}, Type={MessageType}",
            messageId, messageType);

        try
        {
            await HandleAsync(context, context.CancellationToken);

            Logger.LogInformation(
                "Message consumed successfully. MessageId={MessageId}, Type={MessageType}",
                messageId, messageType);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Error consuming message. MessageId={MessageId}, Type={MessageType}. " +
                "Removing claim for retry (best-effort in in-memory mode).",
                messageId, messageType);

            await RemoveClaimInMemoryAsync(messageId);
            throw;
        }
    }

    /// <summary>
    /// In-memory only: attempts to claim a message via EF INSERT.
    /// Returns true if this is the first processing.
    /// Returns false if already processed (unique constraint violation).
    /// </summary>
    private async Task<bool> TryClaimMessageInMemoryAsync(
        Guid messageId,
        string messageType,
        CancellationToken cancellationToken)
    {
        DbContext.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            MessageId = messageId,
            MessageType = messageType,
            ProcessedOnUtc = DateTime.UtcNow
        });

        try
        {
            await DbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Detach the failed entity to keep the context clean
            var entry = DbContext.ChangeTracker.Entries<ProcessedMessage>()
                .FirstOrDefault(e => e.Entity.MessageId == messageId);
            if (entry != null)
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    /// <summary>
    /// In-memory only: removes a claim when processing fails so the message can be retried.
    /// </summary>
    private async Task RemoveClaimInMemoryAsync(Guid messageId)
    {
        try
        {
            var entity = await DbContext.Set<ProcessedMessage>()
                .FindAsync(messageId);
            if (entity != null)
            {
                DbContext.Set<ProcessedMessage>().Remove(entity);
                await DbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Failed to remove idempotency claim for MessageId={MessageId} in in-memory mode.",
                messageId);
        }
    }
}

/// <summary>
/// Entity used to track processed messages for consumer idempotency.
/// </summary>
public sealed class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public DateTime ProcessedOnUtc { get; set; }
}
