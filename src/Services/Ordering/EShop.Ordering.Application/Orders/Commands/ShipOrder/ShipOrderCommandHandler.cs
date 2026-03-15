using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Interfaces;

namespace EShop.Ordering.Application.Orders.Commands.ShipOrder;

public class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand, Result>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ShipOrderCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ShipOrderCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.ShipOrder");
        activity?.SetTag("order.id", request.OrderId.ToString());

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            return Result.Failure(new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."));
        }

        request.UserId = order.UserId;

        order.Ship();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
