using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;

namespace EShop.Ordering.Application.Orders.Commands.DeliverOrder;

/// <summary>
/// Mirrors <c>ShipOrderCommandHandler</c>. The state is checked here and returned as a
/// <c>Result</c> (409) rather than left to <c>Order.Deliver()</c>'s <c>DomainException</c> (400): a request
/// for an order in the wrong state is a conflict, not a malformed request.
/// </summary>
public class DeliverOrderCommandHandler : IRequestHandler<DeliverOrderCommand, Result>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext? _cacheInvalidationContext;

    public DeliverOrderCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext? cacheInvalidationContext = null)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(DeliverOrderCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.DeliverOrder");
        activity?.SetTag("order.id", request.OrderId.ToString());

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            return Result.Failure(new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."));
        }

        _cacheInvalidationContext?.AddFamily(OrderCacheKeys.UserOrders(order.UserId));

        if (order.Status != OrderStatus.Shipped)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "order_not_shipped");
            return Result.Failure(new Error("Order.NotShippedYet", "Only a shipped order can be delivered."));
        }

        order.Deliver();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
