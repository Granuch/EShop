using System.Linq.Expressions;
using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Application.Orders.Queries;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Ordering.Infrastructure.QueryServices;

public class OrderQueryService : IOrderQueryService
{
    private readonly OrderingDbContext _context;

    public OrderQueryService(OrderingDbContext context)
    {
        _context = context;
    }

    public async Task<(List<OrderDto> Items, int TotalCount)> GetOrdersAsync(
        OrderListFilter filter,
        OrderSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.Orders.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var dtos = await Sort(query, sortBy, isDescending)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return (dtos, totalCount);
    }

    public async Task<OrderStatsDto> GetOrderStatsAsync(
        DateTime? from,
        DateTime? to,
        OrderStatsGroupBy groupBy,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_context.Orders.AsNoTracking(), new OrderListFilter(From: from, To: to));

        var counted = await query
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Value = g.Sum(o => o.TotalPrice) })
            .ToListAsync(cancellationToken);

        // Every status, including the ones with no orders: a client charting the breakdown should not
        // have to know which keys the server happened to omit.
        var byStatus = Enum.GetValues<OrderStatus>()
            .Select(status =>
            {
                var row = counted.FirstOrDefault(c => c.Status == status);
                return new OrderStatusBreakdown(status, row?.Count ?? 0, row?.Value ?? 0m);
            })
            .ToList();

        var buckets = await BucketsAsync(query, groupBy, cancellationToken);

        return new OrderStatsDto(
            from,
            to,
            groupBy,
            byStatus.Sum(s => s.Count),
            byStatus.Sum(s => s.Value),
            byStatus.Where(s => PaidRevenueStatuses.Contains(s.Status)).Sum(s => s.Value),
            byStatus.Single(s => s.Status == OrderStatus.Refunded).Value,
            byStatus.Single(s => s.Status == OrderStatus.Cancelled).Value,
            byStatus,
            buckets);
    }

    /// <summary>
    /// The statuses whose totals are money taken and not given back. Refunded and Cancelled are
    /// excluded and reported separately, so no dashboard can show a refund as income.
    /// </summary>
    private static readonly OrderStatus[] PaidRevenueStatuses =
        [OrderStatus.Paid, OrderStatus.Shipped, OrderStatus.Delivered];

    /// <summary>
    /// One <c>GROUP BY</c> on <c>CreatedAt</c>'s date parts. Grouping on integer components rather
    /// than on a truncated timestamp is not a style choice: Npgsql 10's <c>EF.Functions</c> exposes no
    /// <c>date_trunc</c>, and <c>new DateTime(y, m, d)</c> inside a group key is not reliably
    /// translated — the parts always are, and the bucket's start instant is built from them here.
    /// </summary>
    private static async Task<List<OrderStatsBucket>> BucketsAsync(
        IQueryable<Order> query,
        OrderStatsGroupBy groupBy,
        CancellationToken cancellationToken)
    {
        // The paid-revenue sum is spelled out in each branch rather than factored into a helper: EF
        // translates the expression tree, and a call to a method of ours is exactly what it cannot
        // translate. Keep the three predicates identical to PaidRevenueStatuses — the stats test
        // asserts the buckets against the window totals, so a divergence here is caught there.
        var rows = groupBy switch
        {
            OrderStatsGroupBy.Year => await query
                .GroupBy(o => new { o.CreatedAt.Year })
                .Select(g => new BucketRow(
                    g.Key.Year,
                    1,
                    1,
                    g.Count(),
                    g.Sum(o => o.TotalPrice),
                    g.Sum(o => o.Status == OrderStatus.Paid
                        || o.Status == OrderStatus.Shipped
                        || o.Status == OrderStatus.Delivered ? o.TotalPrice : 0m)))
                .ToListAsync(cancellationToken),
            OrderStatsGroupBy.Month => await query
                .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
                .Select(g => new BucketRow(
                    g.Key.Year,
                    g.Key.Month,
                    1,
                    g.Count(),
                    g.Sum(o => o.TotalPrice),
                    g.Sum(o => o.Status == OrderStatus.Paid
                        || o.Status == OrderStatus.Shipped
                        || o.Status == OrderStatus.Delivered ? o.TotalPrice : 0m)))
                .ToListAsync(cancellationToken),
            _ => await query
                .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month, o.CreatedAt.Day })
                .Select(g => new BucketRow(
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    g.Count(),
                    g.Sum(o => o.TotalPrice),
                    g.Sum(o => o.Status == OrderStatus.Paid
                        || o.Status == OrderStatus.Shipped
                        || o.Status == OrderStatus.Delivered ? o.TotalPrice : 0m)))
                .ToListAsync(cancellationToken)
        };

        return rows
            .Select(r => new OrderStatsBucket(
                new DateTime(r.Year, r.Month, r.Day, 0, 0, 0, DateTimeKind.Utc),
                r.OrderCount,
                r.GrossValue,
                r.PaidRevenue))
            .OrderBy(b => b.PeriodStart)
            .ToList();
    }

    private sealed record BucketRow(
        int Year, int Month, int Day, int OrderCount, decimal GrossValue, decimal PaidRevenue);

    public Task<OrderDto?> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(ToDto)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Offset paging only. The cursor mode that lived here (<c>CreatedAt &lt; cursor</c>, with the count
    /// taken before the filter) was removed in audit M4; <c>GetOrdersByUserQueryValidator</c> rejects
    /// a cursor rather than ignoring it.
    /// </summary>
    public async Task<(List<OrderDto> Items, int TotalCount)> GetOrdersByUserAsync(
        string userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var dtos = await NewestFirst(query)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return (dtos, totalCount);
    }

    /// <summary>
    /// A total order. <c>CreatedAt</c> alone is not unique, and offset paging over ties is
    /// nondeterministic on Postgres — an order can appear on two pages or on none. The admin list
    /// ordered by <c>CreatedAt</c> alone; InMemory's stable sort hides that, so no test here can show it.
    /// </summary>
    private static IOrderedQueryable<Order> NewestFirst(IQueryable<Order> query)
        => query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);

    /// <summary>
    /// The admin list's sort (Admin panel S8). <c>Id</c> is the tie-break on every column for the
    /// reason <see cref="NewestFirst"/> gives, and it follows the primary direction so the default
    /// — newest first — stays byte-identical to the SQL <c>ListOrderSqlTests</c> pins.
    /// </summary>
    private static IOrderedQueryable<Order> Sort(IQueryable<Order> query, OrderSortBy sortBy, bool isDescending)
        => sortBy switch
        {
            OrderSortBy.TotalPrice => isDescending
                ? query.OrderByDescending(o => o.TotalPrice).ThenByDescending(o => o.Id)
                : query.OrderBy(o => o.TotalPrice).ThenBy(o => o.Id),
            OrderSortBy.Status => isDescending
                ? query.OrderByDescending(o => o.Status).ThenByDescending(o => o.Id)
                : query.OrderBy(o => o.Status).ThenBy(o => o.Id),
            _ => isDescending
                ? NewestFirst(query)
                : query.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
        };

    /// <summary>
    /// Every narrowing the admin list and the stats read share. Written once so the two cannot
    /// disagree about what "in the window" means.
    /// </summary>
    private static IQueryable<Order> ApplyFilter(IQueryable<Order> query, OrderListFilter filter)
    {
        if (filter.Statuses is { Count: > 0 } statuses)
        {
            // Materialised so EF sees a stable list rather than a possibly-deferred sequence; a union,
            // so the panel's multi-select is one request.
            var wanted = statuses.Distinct().ToArray();
            query = query.Where(o => wanted.Contains(o.Status));
        }

        if (filter.From is { } from)
            query = query.Where(o => o.CreatedAt >= from);

        if (filter.To is { } to)
            query = query.Where(o => o.CreatedAt <= to);

        if (filter.MinTotal is { } minTotal)
            query = query.Where(o => o.TotalPrice >= minTotal);

        if (filter.MaxTotal is { } maxTotal)
            query = query.Where(o => o.TotalPrice <= maxTotal);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var trimmed = filter.Search.Trim();

            // Escape LIKE special characters so a search term cannot inject wildcards — the same
            // treatment Catalog's and Identity's searches give theirs.
            var term = "%" + trimmed
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_") + "%";

            // An order id is matched exactly, not as a substring: uuid has no LIKE in Postgres
            // without a cast, and half a Guid is not a search anyone means.
            var exactId = Guid.TryParse(trimmed, out var parsed) ? parsed : (Guid?)null;

            query = query.Where(o =>
                EF.Functions.ILike(o.UserId, term, "\\")
                || (o.PaymentIntentId != null && EF.Functions.ILike(o.PaymentIntentId, term, "\\"))
                || (exactId != null && o.Id == exactId));
        }

        return query;
    }

    /// <summary>
    /// One projection for every order read — both lists and the single order (audit L5). It used to be
    /// written out three times: twice here and once by hand in <c>GetOrderByIdQueryHandler</c>.
    /// </summary>
    private static readonly Expression<Func<Order, OrderDto>> ToDto = o => new OrderDto
    {
        Id = o.Id,
        UserId = o.UserId,
        TotalPrice = o.TotalPrice,
        Status = o.Status,
        PaymentIntentId = o.PaymentIntentId,
        CreatedAt = o.CreatedAt,
        PaidAt = o.PaidAt,
        ShippedAt = o.ShippedAt,
        DeliveredAt = o.DeliveredAt,
        CancelledAt = o.CancelledAt,
        CancellationReason = o.CancellationReason,
        ShippingAddress = new AddressDto
        {
            Street = o.ShippingAddress.Street,
            City = o.ShippingAddress.City,
            State = o.ShippingAddress.State,
            ZipCode = o.ShippingAddress.ZipCode,
            Country = o.ShippingAddress.Country
        },
        Items = o.Items.Select(i => new OrderItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            SubTotal = i.SubTotal
        }).ToList()
    };
}
