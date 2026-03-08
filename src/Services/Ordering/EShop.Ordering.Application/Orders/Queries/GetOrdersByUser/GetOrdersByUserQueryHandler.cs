using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Ordering.Domain.Interfaces;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

public sealed class GetOrdersByUserQueryHandler : IRequestHandler<GetOrdersByUserQuery, Result<List<OrderDto>>>
{
    private readonly IOrderRepository _orderRepository;

    public GetOrdersByUserQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Result<List<OrderDto>>> Handle(GetOrdersByUserQuery request, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.GetByUserIdAsync(request.UserId, cancellationToken);

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

        return Result<List<OrderDto>>.Success(dtos);
    }
}
