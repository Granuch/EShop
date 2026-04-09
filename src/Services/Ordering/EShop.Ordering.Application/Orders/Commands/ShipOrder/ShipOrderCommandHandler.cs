using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.ShipOrder;

public class ShipOrderCommandHandler : IRequestHandler<ShipOrderCommand, Result>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext? _cacheInvalidationContext;

    public ShipOrderCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext? cacheInvalidationContext = null)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
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

        _cacheInvalidationContext?.AddKey($"orders:user:{order.UserId}");

        if (order.Status != OrderStatus.Paid)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "order_not_paid");
            return Result.Failure(new Error("Order.NotPaidYet", "Order must be paid before shipping."));
        }

        order.Ship();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
