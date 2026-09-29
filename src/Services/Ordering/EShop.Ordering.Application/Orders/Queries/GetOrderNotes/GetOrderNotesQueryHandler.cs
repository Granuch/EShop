using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Ordering.Application.Abstractions;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderNotes;

public sealed class GetOrderNotesQueryHandler
    : IRequestHandler<GetOrderNotesQuery, Result<IReadOnlyList<OrderNoteDto>>>
{
    private readonly IOrderQueryService _orderQueryService;

    public GetOrderNotesQueryHandler(IOrderQueryService orderQueryService)
    {
        _orderQueryService = orderQueryService;
    }

    public async Task<Result<IReadOnlyList<OrderNoteDto>>> Handle(
        GetOrderNotesQuery request, CancellationToken cancellationToken)
    {
        var notes = await _orderQueryService.GetOrderNotesAsync(request.OrderId, cancellationToken);

        // null is "no such order", not "no notes" — an annotated-by-nobody order answers 200 with [].
        return notes is null
            ? Result<IReadOnlyList<OrderNoteDto>>.Failure(
                new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."))
            : Result<IReadOnlyList<OrderNoteDto>>.Success(notes);
    }
}
