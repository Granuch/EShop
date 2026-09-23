using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Auditing;

/// <summary>Persists one audit row, outside whatever unit of work the audited command used.</summary>
public interface IAuditLogWriter
{
    /// <summary>
    /// Writes <paramref name="entry"/> and commits it. Takes no cancellation token on purpose: the row describes a
    /// command that has already run, so a client disconnecting afterwards must not cancel the record of it.
    /// </summary>
    Task WriteAsync(AuditLogEntry entry);

    /// <summary>
    /// Writes every entry of one batch command in a single commit (admin panel S16), so a batch is recorded whole or not
    /// at all rather than half. Same scope and cancellation rules as <see cref="WriteAsync"/>.
    /// </summary>
    Task WriteAllAsync(IReadOnlyCollection<AuditLogEntry> entries);
}

/// <summary>
/// The service's <see cref="IAuditLogWriter"/>: writes through <typeparamref name="TDbContext"/> resolved from its
/// <b>own</b> scope.
///
/// <para>
/// The own scope is the whole of "outside the command transaction" (decision Q8a), and it is not optional. The
/// request's <typeparamref name="TDbContext"/> is exactly the wrong context to write through after a failure: it
/// still tracks whatever the failed command added, so saving the audit row through it would try to save those too;
/// and after a constraint violation on Postgres its connection's transaction is aborted, so the save would fail with
/// <c>25P02</c> and the row describing the failure would be lost with it. A fresh scope has a fresh context and a
/// fresh connection, so the row commits on its own whether the command committed, rolled back, or never reached the
/// database. Payment's <c>FailedStripeWebhookStore</c> is the precedent.
/// </para>
/// </summary>
public sealed class AuditLogWriter<TDbContext> : IAuditLogWriter
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AuditLogWriter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task WriteAsync(AuditLogEntry entry)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        db.Set<AuditLogEntry>().Add(entry);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task WriteAllAsync(IReadOnlyCollection<AuditLogEntry> entries)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        db.Set<AuditLogEntry>().AddRange(entries);
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
