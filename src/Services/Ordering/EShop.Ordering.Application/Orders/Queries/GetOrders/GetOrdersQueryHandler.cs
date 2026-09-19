using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Orders.Queries.GetOrders;

/// <summary>
/// Handler for getting orders with filtering and pagination.
/// Delegates query composition to IOrderQueryService (Infrastructure).
/// Not cached: this is the admin view and should always be current.
/// </summary>
public sealed class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, Result<PagedResult<OrderDto>>>
{
    private readonly IOrderQueryService _orderQueryService;

    public GetOrdersQueryHandler(IOrderQueryService orderQueryService)
    {
        _orderQueryService = orderQueryService;
    }

    public async Task<Result<PagedResult<OrderDto>>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.EffectivePageNumber;
        var pageSize = request.EffectivePageSize;

        // GetOrdersQueryValidator has already rejected anything but a status name, so this cannot fail.
        // It used to TryParse and fall back to "no filter", answering a typo with every order.
        var statuses = request.RequestedStatusNames
            .Select(name => Enum.Parse<OrderStatus>(name, ignoreCase: true))
            .Distinct()
            .ToArray();

        var sortBy = QueryEnums.ParseSortBy(request.SortBy);

        var filter = new OrderListFilter(
            statuses,
            request.Search,
            QueryEnums.AsUtc(request.From),
            QueryEnums.AsUtc(request.To),
            request.MinTotal,
            request.MaxTotal);

        var (dtos, totalCount) = await _orderQueryService.GetOrdersAsync(
            filter, sortBy, request.EffectiveIsDescending, pageNumber, pageSize, cancellationToken);

        var pagedResult = PagedResult<OrderDto>.Create(dtos, pageNumber, pageSize, totalCount);
        return Result<PagedResult<OrderDto>>.Success(pagedResult);
    }
}
