namespace EShop.Basket.Application.Abstractions;

public interface ICheckoutIdempotencyStore
{
    Task<Guid?> GetCompletedCheckoutIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> TryBeginProcessingAsync(string userId, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(string userId, Guid checkoutId, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task ReleaseProcessingAsync(string userId, CancellationToken cancellationToken = default);
}
