using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Application.Orders.Commands.AddOrderNote;

/// <summary>
/// Attaches an internal note to an order (Admin panel S9, endpoint #60).
///
/// <para>
/// There is deliberately no author field. The note's author is the authenticated caller, resolved
/// server-side from <c>ICurrentUserContext</c> — a body-supplied author would let one operator sign
/// another's name to a record whose whole value is that it says who wrote it.
/// </para>
/// <para>
/// <b>Not <c>ICacheInvalidatingCommand</c>, and that is not an omission.</b> Notes appear in no cached
/// response: <c>OrderDto</c> does not carry them, so <c>order:{id}</c> and the per-user list families
/// stay correct, and the notes read itself is uncached. Declaring a key here would evict a correct
/// entry for nothing.
/// </para>
/// </summary>
public record AddOrderNoteCommand : IRequest<Result<Guid>>, ITransactionalCommand
{
    public Guid OrderId { get; init; }

    /// <summary>
    /// Free text an operator writes about a customer's order, so it can contain anything a support
    /// conversation contains — a phone number, an address, a health reason for a return.
    /// <c>LoggingBehavior</c> logs the whole command at Information, which would put all of it in Seq
    /// and the rolling log files.
    /// </summary>
    [SensitiveData] public string Body { get; init; } = string.Empty;
}
