using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Commands.AddItemToBasket;

/// <summary>
/// Handler for adding item to basket
/// </summary>
public class AddItemToBasketCommandHandler : IRequestHandler<AddItemToBasketCommand, Result<Unit>>
{
    // TODO: Inject IBasketRepository, ILogger
    // private readonly IBasketRepository _basketRepository;

    public async Task<Result<Unit>> Handle(AddItemToBasketCommand request, CancellationToken cancellationToken)
    {
        // TODO: Get or create basket for user
        // TODO: Add item to basket (or update quantity if exists)
        // TODO: Save basket to Redis
        // TODO: Return success
        throw new NotImplementedException();
    }
}
