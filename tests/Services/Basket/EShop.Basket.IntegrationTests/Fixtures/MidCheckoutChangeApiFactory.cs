using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// Basket audit S3. Lands a write between checkout's read of the basket and its commit, deterministically — the same
/// "blind the timing" approach as Catalog's <c>BlindSlugCheckApiFactory</c>, because firing concurrent requests only
/// makes such a test flaky, and a green run of one that never raced proves nothing.
///
/// <para>Armed per call: <see cref="AddToBasketAfterNextRead"/> makes the <i>next</i> read of that user's basket
/// return the basket as read, after first saving a copy with one more item. The caller then holds a basket whose
/// stored state has already moved on — exactly what a second tab adding an item mid-checkout produces.</para>
/// </summary>
public sealed class MidCheckoutChangeApiFactory : BasketApiFactory
{
    private readonly object _gate = new();
    private (string UserId, Guid ProductId)? _pending;

    public void AddToBasketAfterNextRead(string userId, Guid productId)
    {
        lock (_gate)
        {
            _pending = (userId, productId);
        }
    }

    private Guid? TakePending(string userId)
    {
        lock (_gate)
        {
            if (_pending is { } pending && pending.UserId == userId)
            {
                _pending = null;
                return pending.ProductId;
            }

            return null;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddScoped<RedisBasketRepository>();
            services.AddScoped<IBasketRepository>(sp =>
                new ChangingRepository(sp.GetRequiredService<RedisBasketRepository>(), this));
        });
    }

    private sealed class ChangingRepository(IBasketRepository inner, MidCheckoutChangeApiFactory factory) : IBasketRepository
    {
        public async Task<ShoppingBasket?> GetBasketAsync(string userId, CancellationToken cancellationToken = default)
        {
            var basket = await inner.GetBasketAsync(userId, cancellationToken);

            if (basket is not null && factory.TakePending(userId) is { } productId)
            {
                var concurrent = await inner.GetBasketAsync(userId, cancellationToken);
                concurrent!.AddItem(productId, "Added mid-checkout", 1m, 1);
                await inner.SaveBasketAsync(concurrent, cancellationToken);
            }

            return basket;
        }

        public Task<ShoppingBasket> SaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default)
            => inner.SaveBasketAsync(basket, cancellationToken);

        public Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default)
            => inner.DeleteBasketAsync(userId, cancellationToken);

        public Task<IReadOnlyCollection<string>> GetUsersContainingProductAsync(Guid productId, CancellationToken cancellationToken = default)
            => inner.GetUsersContainingProductAsync(productId, cancellationToken);
    }
}
