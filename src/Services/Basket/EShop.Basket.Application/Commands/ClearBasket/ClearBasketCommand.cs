using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Basket.Application.Commands.ClearBasket;

public record ClearBasketCommand : IRequest<Result<Unit>>
{
    public string UserId { get; init; } = string.Empty;
}
