using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Ordering.Application.Abstractions;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderStatusHistory;

public sealed class GetOrderStatusHistoryQueryHandler
    : IRequestHandler<GetOrderStatusHistoryQuery, Result<IReadOnlyList<OrderStatusHistoryDto>>>
{
    private readonly IOrderQueryService _orderQueryService;

    public GetOrderStatusHistoryQueryHandler(IOrderQueryService orderQueryService)
    {
        _orderQueryService = orderQueryService;
    }

    public async Task<Result<IReadOnlyList<OrderStatusHistoryDto>>> Handle(
        GetOrderStatusHistoryQuery request, CancellationToken cancellationToken)
    {
        var history = await _orderQueryService.GetOrderStatusHistoryAsync(request.OrderId, cancellationToken);

        return history is null
            ? Result<IReadOnlyList<OrderStatusHistoryDto>>.Failure(
                new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."))
            : Result<IReadOnlyList<OrderStatusHistoryDto>>.Success(history);
    }
}
