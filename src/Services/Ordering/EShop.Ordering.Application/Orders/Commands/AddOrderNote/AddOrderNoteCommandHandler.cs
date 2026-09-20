using System.Diagnostics;
using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Application.Telemetry;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.Interfaces;

namespace EShop.Ordering.Application.Orders.Commands.AddOrderNote;

public class AddOrderNoteCommandHandler : IRequestHandler<AddOrderNoteCommand, Result<Guid>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserContext _currentUserContext;

    public AddOrderNoteCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserContext currentUserContext)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _currentUserContext = currentUserContext;
    }

    public async Task<Result<Guid>> Handle(AddOrderNoteCommand request, CancellationToken cancellationToken)
    {
        using var activity = OrderingActivitySource.Source.StartActivity("Ordering.AddOrderNote");
        activity?.SetTag("order.id", request.OrderId.ToString());

        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            return Result<Guid>.Failure(
                new Error("Order.NotFound", $"Order with ID '{request.OrderId}' was not found."));
        }

        // The cap has to be checked here rather than inside the aggregate: Order._notes is never loaded
        // by GetByIdAsync, so an aggregate-side check would compare against an empty collection and pass
        // for every order forever. See Order.MaxNotes.
        var existing = await _orderRepository.CountNotesAsync(request.OrderId, cancellationToken);
        if (existing >= Order.MaxNotes)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "note_limit_reached");
            return Result<Guid>.Failure(OrderErrors.NoteLimitReached);
        }

        // Server-side, never from the body. UserName is the display name; it falls back to the id
        // rather than to a placeholder, so a note always names someone who can be looked up.
        var authorId = _currentUserContext.UserId;
        var authorName = string.IsNullOrWhiteSpace(_currentUserContext.UserName)
            ? authorId
            : _currentUserContext.UserName;

        // Blank only if no principal reached the handler, which the endpoint's Admin policy already
        // rules out; the aggregate refuses it with a DomainException rather than storing an unsigned note.
        var noteId = order.AddNote(authorId ?? string.Empty, authorName ?? string.Empty, request.Body);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(noteId);
    }
}
