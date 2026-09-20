using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.Infrastructure.QueryServices;

/// <inheritdoc cref="IPaymentQueryService"/>
public class PaymentQueryService : IPaymentQueryService
{
    private readonly PaymentDbContext _context;

    public PaymentQueryService(PaymentDbContext context)
    {
        _context = context;
    }

    public async Task<(List<PaymentTransaction> Items, int TotalCount)> GetPageAsync(
        PaymentListFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.PaymentTransactions.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await NewestFirst(query)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<int> CountAsync(PaymentListFilter filter, CancellationToken cancellationToken = default)
        => ApplyFilter(_context.PaymentTransactions.AsNoTracking(), filter).CountAsync(cancellationToken);

    public Task<List<PaymentTransaction>> ListAsync(
        PaymentListFilter filter,
        int maxRows,
        CancellationToken cancellationToken = default)
        => NewestFirst(ApplyFilter(_context.PaymentTransactions.AsNoTracking(), filter))
            .Take(maxRows)
            .ToListAsync(cancellationToken);

    public async Task<PaymentStats> GetStatsAsync(
        PaymentListFilter window,
        PaymentStatsGroupBy groupBy,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.PaymentTransactions.AsNoTracking(), window);

        var counted = await query
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);

        // Every status, including the ones with no payments: a client charting the breakdown should not have to know
        // which keys the server happened to omit, and the totals below read Refunded and Failed with Single(...).
        var byStatus = Enum.GetValues<PaymentStatus>()
            .Select(status =>
            {
                var row = counted.FirstOrDefault(c => c.Status == status);
                return new PaymentStatusBreakdown(status, row?.Count ?? 0, row?.Amount ?? 0m);
            })
            .ToList();

        var buckets = await BucketsAsync(query, groupBy, cancellationToken);

        return new PaymentStats(
            window.From,
            window.To,
            window.Currency ?? string.Empty,
            groupBy,
            byStatus.Sum(s => s.Count),
            byStatus.Sum(s => s.Amount),
            byStatus.Single(s => s.Status == PaymentStatus.Success).Amount,
            byStatus.Single(s => s.Status == PaymentStatus.Refunded).Amount,
            byStatus.Single(s => s.Status == PaymentStatus.Failed).Amount,
            byStatus,
            buckets);
    }

    public Task<bool> PaymentExistsAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => _context.PaymentTransactions.AsNoTracking().AnyAsync(p => p.Id == paymentId, cancellationToken);

    public Task<List<PaymentEvent>> GetEventsAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => _context.PaymentEvents
            .AsNoTracking()
            .Where(e => e.PaymentTransactionId == paymentId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// One <c>GROUP BY</c> on <c>CreatedAt</c>'s date parts. Grouping on integer components rather than on a truncated
    /// timestamp is not a style choice: Npgsql 10's <c>EF.Functions</c> exposes no <c>date_trunc</c>, and a bare
    /// integer in a Postgres <c>GROUP BY</c> is read as an ordinal column reference, so a key mixing a column with a
    /// constant is worse than it looks. The bucket's start instant is assembled from the parts here.
    /// </summary>
    private static async Task<List<PaymentStatsBucket>> BucketsAsync(
        IQueryable<PaymentTransaction> query,
        PaymentStatsGroupBy groupBy,
        CancellationToken cancellationToken)
    {
        // The captured-revenue and refunded sums are spelled out in each branch rather than factored into a helper: EF
        // translates the expression tree, and a call to a method of ours is exactly what it cannot translate. Keep them
        // identical to the window totals above — PaymentStatsTests asserts the buckets sum to those totals, so a
        // divergence here is caught from the other side.
        var rows = groupBy switch
        {
            PaymentStatsGroupBy.Year => await query
                .GroupBy(p => new { p.CreatedAt.Year })
                .Select(g => new BucketRow(
                    g.Key.Year,
                    1,
                    1,
                    g.Count(),
                    g.Sum(p => p.Amount),
                    g.Sum(p => p.Status == PaymentStatus.Success ? p.Amount : 0m),
                    g.Sum(p => p.Status == PaymentStatus.Refunded ? p.Amount : 0m)))
                .ToListAsync(cancellationToken),
            PaymentStatsGroupBy.Month => await query
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
                .Select(g => new BucketRow(
                    g.Key.Year,
                    g.Key.Month,
                    1,
                    g.Count(),
                    g.Sum(p => p.Amount),
                    g.Sum(p => p.Status == PaymentStatus.Success ? p.Amount : 0m),
                    g.Sum(p => p.Status == PaymentStatus.Refunded ? p.Amount : 0m)))
                .ToListAsync(cancellationToken),
            _ => await query
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month, p.CreatedAt.Day })
                .Select(g => new BucketRow(
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    g.Count(),
                    g.Sum(p => p.Amount),
                    g.Sum(p => p.Status == PaymentStatus.Success ? p.Amount : 0m),
                    g.Sum(p => p.Status == PaymentStatus.Refunded ? p.Amount : 0m)))
                .ToListAsync(cancellationToken)
        };

        return rows
            .Select(r => new PaymentStatsBucket(
                new DateTime(r.Year, r.Month, r.Day, 0, 0, 0, DateTimeKind.Utc),
                r.PaymentCount,
                r.GrossAmount,
                r.CapturedRevenue,
                r.RefundedAmount))
            .OrderBy(b => b.PeriodStart)
            .ToList();
    }

    private sealed record BucketRow(
        int Year,
        int Month,
        int Day,
        int PaymentCount,
        decimal GrossAmount,
        decimal CapturedRevenue,
        decimal RefundedAmount);

    /// <summary>
    /// A total order. <c>CreatedAt</c> alone is not unique, and offset paging over ties is nondeterministic on
    /// Postgres; InMemory's stable sort hides that, so no test on this suite's default host can show it.
    /// </summary>
    private static IOrderedQueryable<PaymentTransaction> NewestFirst(IQueryable<PaymentTransaction> query)
        => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);

    /// <summary>
    /// Every narrowing the list, the export and the stats window share. Written once so they cannot disagree about
    /// what "in the window" means.
    /// <para>Note what is <b>not</b> here: the method-<see cref="PaymentMethodType.None"/> exclusion that
    /// <c>PaymentRepository.ForUserList</c> applies. That placeholder is hidden from a customer because nothing was
    /// ever started or charged for it; an admin list that hid it would hide the record that stops a late
    /// <c>OrderCreatedEvent</c> charging a cancelled order, which is precisely the row an operator opens this screen
    /// to find.</para>
    /// </summary>
    private static IQueryable<PaymentTransaction> ApplyFilter(
        IQueryable<PaymentTransaction> query,
        PaymentListFilter filter)
    {
        if (filter.Statuses is { Count: > 0 } statuses)
        {
            // Materialised so EF sees a stable list rather than a possibly-deferred sequence; a union, so the panel's
            // multi-select is one request.
            var wanted = statuses.Distinct().ToArray();
            query = query.Where(p => wanted.Contains(p.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.UserId))
            query = query.Where(p => p.UserId == filter.UserId);

        if (filter.OrderId is { } orderId)
            query = query.Where(p => p.OrderId == orderId);

        if (filter.PaymentMethod is { } method)
            query = query.Where(p => p.PaymentMethod == method);

        if (!string.IsNullOrWhiteSpace(filter.Currency))
            query = query.Where(p => p.Currency == filter.Currency);

        if (filter.From is { } from)
            query = query.Where(p => p.CreatedAt >= from);

        if (filter.To is { } to)
            query = query.Where(p => p.CreatedAt <= to);

        if (filter.MinAmount is { } minAmount)
            query = query.Where(p => p.Amount >= minAmount);

        if (filter.MaxAmount is { } maxAmount)
            query = query.Where(p => p.Amount <= maxAmount);

        return query;
    }
}
