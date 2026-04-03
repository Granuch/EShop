using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Ordering.Application.Abstractions;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

public sealed class GetOrdersByUserQueryHandler : IRequestHandler<GetOrdersByUserQuery, Result<PagedResult<OrderDto>>>
{
    private readonly IOrderQueryService _orderQueryService;

    public GetOrdersByUserQueryHandler(IOrderQueryService orderQueryService)
    {
        _orderQueryService = orderQueryService;
    }

    public async Task<Result<PagedResult<OrderDto>>> Handle(GetOrdersByUserQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.EffectivePageNumber;
        var pageSize = request.EffectivePageSize;

        var (dtos, totalCount) = await _orderQueryService.GetOrdersByUserAsync(
            request.UserId,
            pageNumber,
            pageSize,
            request.Cursor,
            cancellationToken);

        var pagedResult = PagedResult<OrderDto>.Create(dtos, pageNumber, pageSize, totalCount);
        return Result<PagedResult<OrderDto>>.Success(pagedResult);
    }
}
