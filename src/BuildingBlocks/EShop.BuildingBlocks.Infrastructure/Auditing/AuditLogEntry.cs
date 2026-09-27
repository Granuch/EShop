namespace EShop.BuildingBlocks.Infrastructure.Auditing;

/// <summary>How an audited command ended, as its caller saw it.</summary>
public enum AuditOutcome
{
    /// <summary>The handler returned a successful <c>Result</c>.</summary>
    Succeeded,

    /// <summary>
    /// The handler returned a failed <c>Result</c>. <b>Not</b> the same as "nothing changed": <c>TransactionBehavior</c>
    /// commits on any non-exception return, so a rejected command may still have written.
    /// </summary>
    Rejected,

    /// <summary>The pipeline threw. The error code is the exception's type name, never its message.</summary>
    Failed
}

/// <summary>
/// One row of a service's admin audit trail (<c>audit_log</c>, migration M11). Written by
/// <see cref="AuditBehavior{TRequest,TResponse}"/>; never updated or deleted by the application.
///
/// <para>
/// <b>The key is a database-generated <c>bigint</c>, not a <c>Guid</c>,</b> because the key is also the paging
/// cursor. A <c>Guid</c> cursor would need Postgres's <c>uuid</c> order and .NET's <c>Guid.CompareTo</c> order to
/// agree, and they do not — Postgres compares the bytes in RFC order, .NET compares its first three fields as
/// little-endian integers. An identity column is also insertion order, which is what "newest first" means.
/// </para>
///
/// <para>
/// There is no <c>CreatedAt</c>/<c>CreatedBy</c>: <c>BaseDbContext.SetAuditFields</c> overwrites properties with
/// those names on every save, and the actor here is the command's caller, captured in the request scope — not
/// whoever the audit writer's own scope would name.
/// </para>
/// </summary>
public sealed class AuditLogEntry
{
    public const int ServiceMaxLength = 50;
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 200;
    public const int ActorUserIdMaxLength = 200;
    public const int ActorNameMaxLength = 256;
    public const int CorrelationIdMaxLength = 100;
    public const int ErrorCodeMaxLength = 200;

    private AuditLogEntry()
    {
    }

    public long Id { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string Service { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    public string? ActorUserId { get; private set; }
    public string? ActorName { get; private set; }
    public string? CorrelationId { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? PayloadJson { get; private set; }

    /// <summary>
    /// Builds a row, cutting every bounded field to its column length. <b>The cut is the point, not a nicety</b>: the
    /// correlation id comes from a client-supplied <c>X-Correlation-ID</c> header, so an over-long value must shorten
    /// the row rather than fail the insert — a failed insert is a lost audit record for a command that already ran.
    /// </summary>
    public static AuditLogEntry Record(
        DateTime occurredAt,
        string service,
        string action,
        string entityType,
        string? entityId,
        string? actorUserId,
        string? actorName,
        string? correlationId,
        AuditOutcome outcome,
        string? errorCode,
        string? payloadJson) => new()
        {
            OccurredAt = occurredAt,
            Service = Cut(service, ServiceMaxLength)!,
            Action = Cut(action, ActionMaxLength)!,
            EntityType = Cut(entityType, EntityTypeMaxLength)!,
            EntityId = Cut(entityId, EntityIdMaxLength),
            ActorUserId = Cut(actorUserId, ActorUserIdMaxLength),
            ActorName = Cut(actorName, ActorNameMaxLength),
            CorrelationId = Cut(correlationId, CorrelationIdMaxLength),
            Outcome = outcome,
            ErrorCode = Cut(errorCode, ErrorCodeMaxLength),
            PayloadJson = payloadJson
        };

    private static string? Cut(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
