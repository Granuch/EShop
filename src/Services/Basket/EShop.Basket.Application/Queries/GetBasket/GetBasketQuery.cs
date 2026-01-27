using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Queries.GetBasket;

/// <summary>
/// Query to get user's basket
/// </summary>
public record GetBasketQuery : IRequest<Result<BasketDto?>>
{
    public string UserId { get; init; } = string.Empty;
}
