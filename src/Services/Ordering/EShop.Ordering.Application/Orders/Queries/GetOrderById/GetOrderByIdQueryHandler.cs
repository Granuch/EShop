using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Ordering.Application.Abstractions;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderById;

/// <summary>
/// Reads through <see cref="IOrderQueryService"/>, whose projection the lists share (audit L5). This used to
/// load the aggregate with its items and map every field by hand — a third copy of the same mapping.
/// </summary>
public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, Result<OrderDto>>
{
    private readonly IOrderQueryService _orderQueryService;

    public GetOrderByIdQueryHandler(IOrderQueryService orderQueryService)
    {
        _orderQueryService = orderQueryService;
    }

    public async Task<Result<OrderDto>> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _orderQueryService.GetOrderByIdAsync(request.OrderId, cancellationToken);

        return order is null
            ? Result<OrderDto>.Failure(new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."))
            : Result<OrderDto>.Success(order);
    }
}
