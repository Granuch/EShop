using MassTransit;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Ordering.Application.Consumers;

/// <summary>
/// Consumer for BasketCheckedOutEvent
/// </summary>
public class BasketCheckedOutConsumer : IConsumer<BasketCheckedOutEvent>
{
    // TODO: Inject IMediator, ILogger
    // private readonly IMediator _mediator;
    // private readonly ILogger<BasketCheckedOutConsumer> _logger;

    public async Task Consume(ConsumeContext<BasketCheckedOutEvent> context)
    {
        // TODO: Log received event
        // TODO: Map event to CreateOrderCommand
        // TODO: Send command via MediatR
        // TODO: Handle success/failure
        throw new NotImplementedException();
    }
}
