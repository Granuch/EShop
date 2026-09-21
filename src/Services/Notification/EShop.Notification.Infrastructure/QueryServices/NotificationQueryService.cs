using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Notification.Infrastructure.QueryServices;

/// <inheritdoc cref="INotificationQueryService"/>
public sealed class NotificationQueryService : INotificationQueryService
{
    private readonly NotificationDbContext _context;

    public NotificationQueryService(NotificationDbContext context)
    {
        _context = context;
    }

    public async Task<(List<NotificationLog> Items, int TotalCount)> GetPageAsync(
        NotificationJournalFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.NotificationLogs.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<NotificationLog?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.NotificationLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<NotificationJournalStats> GetStatsAsync(
        NotificationJournalFilter window,
        CancellationToken cancellationToken = default)
    {
        var counted = await ApplyFilter(_context.NotificationLogs.AsNoTracking(), window)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Every status, including the ones with no rows: a client charting the breakdown should not have to know which
        // keys the server happened to omit, and the named figures below read them with Single(...).
        var byStatus = Enum.GetValues<NotificationStatus>()
            .Select(status => new NotificationStatusCount(
                status,
                counted.FirstOrDefault(c => c.Status == status)?.Count ?? 0))
            .ToList();

        int CountOf(NotificationStatus status) => byStatus.Single(s => s.Status == status).Count;

        return new NotificationJournalStats(
            window.From,
            window.To,
            byStatus.Sum(s => s.Count),
            CountOf(NotificationStatus.Sent),
            CountOf(NotificationStatus.Failed),
            // "Queued" is what an operator means by it: nothing has been delivered yet and something still will be.
            // Pending is a row whose first attempt has not started; Sending is one an attempt holds. Neither is final.
            CountOf(NotificationStatus.Pending) + CountOf(NotificationStatus.Sending),
            CountOf(NotificationStatus.Undeliverable),
            byStatus);
    }

    /// <remarks>
    /// Every string comparison goes through <c>ToLower()</c> rather than <c>EF.Functions.ILike</c>: ILike is Npgsql-only
    /// and this same query runs on EF InMemory in the HTTP suite, where it would throw. <c>ToLower()</c> translates to
    /// <c>lower(...)</c> on Postgres and runs natively in memory.
    /// </remarks>
    private static IQueryable<NotificationLog> ApplyFilter(
        IQueryable<NotificationLog> query,
        NotificationJournalFilter filter)
    {
        if (filter.Statuses.Length > 0)
        {
            query = query.Where(x => filter.Statuses.Contains(x.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.EventType))
        {
            var eventType = filter.EventType.Trim().ToLowerInvariant();
            query = query.Where(x => x.EventType.ToLower() == eventType);
        }

        if (!string.IsNullOrWhiteSpace(filter.TemplateName))
        {
            var templateName = filter.TemplateName.Trim().ToLowerInvariant();
            query = query.Where(x => x.TemplateName.ToLower() == templateName);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            var userId = filter.UserId.Trim();
            query = query.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(filter.RecipientEmail))
        {
            var email = filter.RecipientEmail.Trim().ToLowerInvariant();
            query = query.Where(x => x.RecipientEmail != null && x.RecipientEmail.ToLower() == email);
        }

        if (filter.From.HasValue)
        {
            var from = filter.From.Value;
            query = query.Where(x => x.CreatedAt >= from);
        }

        if (filter.To.HasValue)
        {
            var to = filter.To.Value;
            query = query.Where(x => x.CreatedAt <= to);
        }

        if (filter.HasError == true)
        {
            query = query.Where(x => x.LastError != null);
        }
        else if (filter.HasError == false)
        {
            query = query.Where(x => x.LastError == null);
        }

        return query;
    }
}
