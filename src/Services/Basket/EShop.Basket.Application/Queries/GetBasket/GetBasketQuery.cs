using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Queries.GetBasket;

/// <summary>
/// Query to get user's basket. Always answers a basket: an empty one for a user who has none (Basket audit S8, D8).
///
/// <para><b>Deliberately not cached (Basket audit S5, D5).</b> It used to be an <c>ICacheableQuery</c>: a second Redis
/// copy of a Redis document, costing the same one round trip as reading the basket itself. The price-sync consumer
/// writes through the repository, not MediatR, so it never evicted that copy, and <c>GET</c> showed an old price for up
/// to two minutes while checkout charged the new one.</para>
/// </summary>
public record GetBasketQuery : IRequest<Result<BasketDto>>
{
    public string UserId { get; init; } = string.Empty;
}
