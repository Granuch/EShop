using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.Application.Orders.Commands.UpdateShippingAddress;

public class UpdateShippingAddressCommandHandler : IRequestHandler<UpdateShippingAddressCommand, Result>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext? _cacheInvalidationContext;

    public UpdateShippingAddressCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext? cacheInvalidationContext = null)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(UpdateShippingAddressCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.UpdateShippingAddress");
        activity?.SetTag("order.id", request.OrderId.ToString());

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            return Result.Failure(new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."));
        }

        // Pre-checked here rather than left to the aggregate's DomainException, which the middleware
        // maps to 400: the request is well-formed and the order is simply past the point where it
        // applies, which is a 409 — the same reading OrderItemErrors.NotModifiable has.
        if (order.Status is not (OrderStatus.Pending or OrderStatus.Paid))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "address_not_modifiable");
            return Result.Failure(OrderErrors.AddressNotModifiable(order.Status));
        }

        _cacheInvalidationContext?.AddFamily(OrderCacheKeys.UserOrders(order.UserId));

        // Constructed after the state check, not before: Address validates in its constructor and
        // throws, and TransactionBehavior commits on any non-exception return — so anything that can
        // reject the request must run before the aggregate is touched.
        order.UpdateShippingAddress(new Address(
            request.Street, request.City, request.State, request.ZipCode, request.Country));

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
