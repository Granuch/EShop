using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Orders;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;

public class RemoveOrderItemCommandHandler : IRequestHandler<RemoveOrderItemCommand, Result>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext? _cacheInvalidationContext;

    public RemoveOrderItemCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext? cacheInvalidationContext = null)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(RemoveOrderItemCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.RemoveOrderItem");
        activity?.SetTag("order.id", request.OrderId.ToString());
        activity?.SetTag("item.id", request.ItemId.ToString());

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            return Result.Failure(new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."));
        }

        if (order.Status != OrderStatus.Pending)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_modifiable");
            return Result.Failure(OrderItemErrors.NotModifiable(order.Status));
        }

        // Order.RemoveItem throws a DomainException for a missing item, which the middleware maps to
        // 400; a missing sub-resource is a 404, so answer it here first.
        if (order.Items.All(i => i.Id != request.ItemId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "item_not_found");
            return Result.Failure(new Error(
                "OrderItem.NotFound",
                $"Item '{request.ItemId}' was not found on order '{request.OrderId}'."));
        }

        _cacheInvalidationContext?.AddFamily(OrderCacheKeys.UserOrders(order.UserId));

        order.RemoveItem(request.ItemId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
