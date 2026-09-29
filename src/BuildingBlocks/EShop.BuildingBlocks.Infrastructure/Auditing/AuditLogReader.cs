using Microsoft.EntityFrameworkCore;

namespace EShop.BuildingBlocks.Infrastructure.Auditing;

/// <summary>A validated request for one page of a service's audit trail. Every filter is an exact match.</summary>
public sealed record AuditLogFilter(
    long? Before,
    int PageSize,
    string? ActorUserId,
    string? Action,
    string? EntityType,
    string? EntityId,
    AuditOutcome? Outcome,
    DateTime? From,
    DateTime? To);

/// <summary>One audit row as the API returns it.</summary>
public sealed record AuditLogEntryDto(
    long Id,
    DateTime OccurredAt,
    string Service,
    string Action,
    string EntityType,
    string? EntityId,
    string? ActorUserId,
    string? ActorName,
    string? CorrelationId,
    string Outcome,
    string? ErrorCode,
    string? PayloadJson);

/// <summary>
/// One page of a service's audit trail, newest first. <see cref="NextBefore"/> is the <c>before</c> value for the next
/// page, or <c>null</c> when this page reached the oldest row.
/// </summary>
public sealed record AuditLogPageDto(IReadOnlyList<AuditLogEntryDto> Items, long? NextBefore);

/// <summary>Reads a service's own <c>audit_log</c>.</summary>
public interface IAuditLogReader
{
    Task<AuditLogPageDto> ReadAsync(AuditLogFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// Keyset paging on the identity key, newest first: a page is the <c>PageSize</c> highest ids below <c>Before</c>.
/// Rows written while an operator pages land above the first page's ids and so never shift a later page — the reason
/// the cursor is an id, not an offset.
/// </summary>
public sealed class AuditLogReader<TDbContext> : IAuditLogReader
    where TDbContext : DbContext
{
    private readonly TDbContext _db;

    public AuditLogReader(TDbContext db)
    {
        _db = db;
    }

    public async Task<AuditLogPageDto> ReadAsync(AuditLogFilter filter, CancellationToken cancellationToken)
    {
        var query = _db.Set<AuditLogEntry>().AsNoTracking();

        if (filter.Before is { } before)
            query = query.Where(e => e.Id < before);
        if (filter.ActorUserId is { } actor)
            query = query.Where(e => e.ActorUserId == actor);
        if (filter.Action is { } action)
            query = query.Where(e => e.Action == action);
        if (filter.EntityType is { } entityType)
            query = query.Where(e => e.EntityType == entityType);
        if (filter.EntityId is { } entityId)
            query = query.Where(e => e.EntityId == entityId);
        if (filter.Outcome is { } outcome)
            query = query.Where(e => e.Outcome == outcome);
        if (filter.From is { } from)
            query = query.Where(e => e.OccurredAt >= from);
        if (filter.To is { } to)
            query = query.Where(e => e.OccurredAt < to);

        // One extra row answers "is there another page?" without a COUNT.
        var rows = await query
            .OrderByDescending(e => e.Id)
            .Take(filter.PageSize + 1)
            .ToListAsync(cancellationToken);

        var page = rows.Take(filter.PageSize).Select(ToDto).ToList();
        var nextBefore = rows.Count > filter.PageSize ? page[^1].Id : (long?)null;

        return new AuditLogPageDto(page, nextBefore);
    }

    private static AuditLogEntryDto ToDto(AuditLogEntry e) => new(
        e.Id,
        DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc),
        e.Service,
        e.Action,
        e.EntityType,
        e.EntityId,
        e.ActorUserId,
        e.ActorName,
        e.CorrelationId,
        e.Outcome.ToString(),
        e.ErrorCode,
        e.PayloadJson);
}
