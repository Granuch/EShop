using EShop.Basket.Domain.Entities;

namespace EShop.Basket.Domain.Interfaces;

/// <summary>
/// Repository interface for basket storage (Redis-based)
/// </summary>
public interface IBasketRepository
{
    // TODO: Implement Get basket by userId
    Task<ShoppingBasket?> GetBasketAsync(string userId, CancellationToken cancellationToken = default);

    // TODO: Implement Save basket to Redis with TTL
    Task<ShoppingBasket> SaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default);

    // TODO: Implement Delete basket
    Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default);

    // TODO: Add TTL configuration (e.g., 7 days for inactive baskets)
}
