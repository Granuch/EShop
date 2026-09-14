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
        OrderStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Order> query = _context.Orders.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var dtos = await NewestFirst(query)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return (dtos, totalCount);
    }

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
