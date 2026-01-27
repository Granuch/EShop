using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Handler for creating an order
/// </summary>
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    // TODO: Inject IOrderRepository, IPublishEndpoint, ILogger
    // private readonly IOrderRepository _orderRepository;
    // private readonly IPublishEndpoint _publishEndpoint;

    public async Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // TODO: Parse shipping address to Address value object
        // TODO: Create OrderItems from request
        // TODO: Create Order aggregate using factory method
        // TODO: Save order to database
        // TODO: Publish OrderCreatedEvent to RabbitMQ (for payment processing)
        // TODO: Return order ID
        throw new NotImplementedException();
    }
}
