using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;

public class CreateCheckedOutOrderCommandHandler : IRequestHandler<CreateCheckedOutOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCheckedOutOrderCommandHandler(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCheckedOutOrderCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.CreateCheckedOutOrder");
        activity?.SetTag("order.user_id", request.UserId);
        activity?.SetTag("order.item_count", request.Items.Count);

        var address = new Address(
            request.Street,
            request.City,
            request.State,
            request.ZipCode,
            request.Country);

        var items = request.Items.Select(i =>
            new OrderItem(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity));

        var order = Order.Create(request.UserId, address, items);

        await _orderRepository.AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("order.id", order.Id.ToString());
        activity?.SetTag("order.total", order.TotalPrice);

        return Result<Guid>.Success(order.Id);
    }
}
