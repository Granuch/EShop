using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Handler for creating an order
/// </summary>
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateOrderCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.CreateOrder");
        activity?.SetTag("order.user_id", request.UserId);
        activity?.SetTag("order.item_count", request.Items.Count);

        var address = new Address(
            request.Street,
            request.City,
            request.State,
            request.ZipCode,
            request.Country);

        var orderItems = request.Items.Select(i =>
            new OrderItem(i.ProductId, i.ProductName, i.Price, i.Quantity));

        var order = Order.Create(request.UserId, address, orderItems);

        await _orderRepository.AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("order.id", order.Id.ToString());
        activity?.SetTag("order.total", order.TotalPrice);

        return Result<Guid>.Success(order.Id);
    }
}
