using EShop.Basket.Domain.Entities;

namespace EShop.Basket.Domain.Interfaces;

/// <summary>
/// Repository interface for basket storage (Redis-based)
/// </summary>
public interface IBasketRepository
{
    Task<ShoppingBasket?> GetBasketAsync(string userId, CancellationToken cancellationToken = default);

    Task<ShoppingBasket> SaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default);

    Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetUsersContainingProductAsync(Guid productId, CancellationToken cancellationToken = default);
}
