using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Handler for basket checkout
/// </summary>
public class CheckoutBasketCommandHandler : IRequestHandler<CheckoutBasketCommand, Result<Guid>>
{
    // TODO: Inject IBasketRepository, IPublishEndpoint (MassTransit), ILogger
    // private readonly IBasketRepository _basketRepository;
    // private readonly IPublishEndpoint _publishEndpoint;

    public async Task<Result<Guid>> Handle(CheckoutBasketCommand request, CancellationToken cancellationToken)
    {
        // TODO: Get basket for user
        // TODO: Validate basket is not empty
        // TODO: Call basket.Checkout() to get BasketCheckedOutEvent
        // TODO: Publish BasketCheckedOutEvent to RabbitMQ
        // TODO: Delete basket from Redis
        // TODO: Return order ID (from event or generate new one)
        throw new NotImplementedException();
    }
}
