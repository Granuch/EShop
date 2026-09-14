using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Basket.Application.Commands.RemoveBasketItem;

public record RemoveBasketItemCommand : IRequest<Result<Unit>>
{
    public string UserId { get; init; } = string.Empty;
    public Guid ProductId { get; init; }
}
