using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Ordering.Application.Abstractions;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderStats;

public sealed class GetOrderStatsQueryHandler : IRequestHandler<GetOrderStatsQuery, Result<OrderStatsDto>>
{
    private readonly IOrderQueryService _orderQueryService;

    public GetOrderStatsQueryHandler(IOrderQueryService orderQueryService)
    {
        _orderQueryService = orderQueryService;
    }

    public async Task<Result<OrderStatsDto>> Handle(GetOrderStatsQuery request, CancellationToken cancellationToken)
    {
        // The validator has already rejected an unknown name, so the fallback here is the omitted case.
        var groupBy = QueryEnums.ParseGroupBy(request.GroupBy);

        var stats = await _orderQueryService.GetOrderStatsAsync(
            QueryEnums.AsUtc(request.From),
            QueryEnums.AsUtc(request.To),
            groupBy,
            cancellationToken);

        return Result<OrderStatsDto>.Success(stats);
    }
}
