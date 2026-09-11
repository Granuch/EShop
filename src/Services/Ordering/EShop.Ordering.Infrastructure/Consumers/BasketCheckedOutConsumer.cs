using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.BuildingBlocks.Infrastructure.Consumers;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;
using EShop.Ordering.Infrastructure.Data;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Infrastructure.Consumers;

/// <summary>
/// Idempotent consumer for BasketCheckedOutEvent.
/// Creates an order from the checked-out basket.
///
/// <para>
/// Every way this can fail ends in the error queue with the message intact — never in an
/// acknowledged message with only a log line behind it. By the time this runs, Basket has already
/// cleared the basket, so the message is the only remaining record of what the customer bought.
/// </para>
/// </summary>
public class BasketCheckedOutConsumer : IdempotentConsumer<BasketCheckedOutEvent, OrderingDbContext>
{
    private readonly IMediator _mediator;

    public BasketCheckedOutConsumer(
        OrderingDbContext dbContext,
        IMediator mediator,
        ILogger<BasketCheckedOutConsumer> logger)
        : base(dbContext, logger)
    {
        _mediator = mediator;
    }

    protected override async Task HandleAsync(ConsumeContext<BasketCheckedOutEvent> context, CancellationToken cancellationToken)
    {
        var message = context.Message;

        Logger.LogInformation(
            "Processing BasketCheckedOutEvent for UserId={UserId}, TotalPrice={TotalPrice}",
            message.UserId,
            message.TotalPrice);

        // Audit C2. The free-text ShippingAddress used to be comma-split here, with "Unknown" and
        // "00000" filling missing parts — values Address rejects — so most real checkouts failed every
        // retry and dead-lettered. Basket always sends the structured form now; a message without it
        // cannot become a correct order, and guessing an address means shipping to the wrong place.
        var address = message.ShippingAddressDetails
            ?? throw new InvalidCheckoutEventException(
                $"BasketCheckedOutEvent {message.EventId} for UserId={message.UserId} has no ShippingAddressDetails.");

        // CreateCheckedOutOrderCommand, not CreateOrderCommand: Basket priced these lines from Catalog
        // on the server, and they are what the customer saw. The HTTP path reprices from Catalog.
        var command = new CreateCheckedOutOrderCommand
        {
            UserId = message.UserId,
            Street = address.Street,
            City = address.City,
            State = address.State,
            ZipCode = address.ZipCode,
            Country = address.Country,
            Items = message.Items.Select(i => new CheckedOutOrderItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                UnitPrice = i.Price,
                Quantity = i.Quantity
            }).ToList()
        };

        Result<Guid> result;
        try
        {
            result = await _mediator.Send(command, cancellationToken);
        }
        catch (DomainException ex)
        {
            // e.g. the same product on two lines. Deterministic, so retrying is pointless. (An invalid
            // address throws ArgumentException from Address, which is already not retried.)
            throw new InvalidCheckoutEventException(
                $"BasketCheckedOutEvent {message.EventId} was rejected by the order domain: {ex.Message}", ex);
        }

        // Audit H5. Validation failures come back as a Result, not an exception. This used to log the
        // failure and return — so IdempotentConsumer committed the claim, the message was acknowledged,
        // and the checkout was gone.
        if (result.IsFailure)
        {
            throw new InvalidCheckoutEventException(
                $"BasketCheckedOutEvent {message.EventId} was rejected: {result.Error!.Code}: {result.Error.Message}");
        }

        Logger.LogInformation(
            "Order {OrderId} created from BasketCheckedOutEvent for UserId={UserId}",
            result.Value, message.UserId);
    }
}
