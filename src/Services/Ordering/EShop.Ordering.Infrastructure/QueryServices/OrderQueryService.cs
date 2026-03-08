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
        IQueryable<Order> query = _context.Orders
            .Include(o => o.Items);

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var dtos = orders.Select(o => new OrderDto
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
        }).ToList();

        return (dtos, totalCount);
    }
}
