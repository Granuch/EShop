using EShop.Basket.Domain.Entities;
using EShop.Basket.Domain.Interfaces;
using EShop.Basket.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Basket.IntegrationTests.Fixtures;

/// <summary>
/// Basket audit S3/S4. Lands a concurrent write between a read of the basket and the write that follows it,
/// deterministically — the same "blind the timing" approach as Catalog's <c>BlindSlugCheckApiFactory</c>, because firing
/// concurrent requests only makes such a test flaky, and a green run of one that never raced proves nothing.
///
/// <para>Armed per call: <see cref="AfterNextRead"/> makes the <i>next</i> read of that user's basket return the basket
/// as read, after first storing a fresh copy with the concurrent change applied. The caller then holds a basket whose
/// stored state has already moved on — what a second tab, a price sync or a checkout produces mid-write. Every later
/// read passes straight through, which is what a retry on a fresh read relies on.</para>
/// </summary>
public sealed class InterleavedWriteApiFactory : BasketApiFactory
{
    private readonly object _gate = new();
    private (string UserId, Action<ShoppingBasket> Change)? _pending;

    public void AfterNextRead(string userId, Action<ShoppingBasket> concurrentChange)
    {
        lock (_gate)
        {
            _pending = (userId, concurrentChange);
        }
    }

    public void AddToBasketAfterNextRead(string userId, Guid productId)
        => AfterNextRead(userId, basket => basket.AddItem(productId, "Added concurrently", 1m, 1));

    private Action<ShoppingBasket>? TakePending(string userId)
    {
        lock (_gate)
        {
            if (_pending is { } pending && pending.UserId == userId)
            {
                _pending = null;
                return pending.Change;
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
                new InterleavingRepository(sp.GetRequiredService<RedisBasketRepository>(), this));
        });
    }

    private sealed class InterleavingRepository(IBasketRepository inner, InterleavedWriteApiFactory factory) : IBasketRepository
    {
        public async Task<ShoppingBasket?> GetBasketAsync(string userId, CancellationToken cancellationToken = default)
        {
            var basket = await inner.GetBasketAsync(userId, cancellationToken);

            if (factory.TakePending(userId) is { } change)
            {
                var concurrent = await inner.GetBasketAsync(userId, cancellationToken) ?? ShoppingBasket.Create(userId);
                change(concurrent);

                if (!await inner.TrySaveBasketAsync(concurrent, cancellationToken))
                {
                    throw new InvalidOperationException("The interleaved write itself lost a race; the test is not deterministic.");
                }
            }

            return basket;
        }

        public Task<bool> TrySaveBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default)
            => inner.TrySaveBasketAsync(basket, cancellationToken);

        public Task<bool> TryDeleteBasketAsync(ShoppingBasket basket, CancellationToken cancellationToken = default)
            => inner.TryDeleteBasketAsync(basket, cancellationToken);

        public Task<bool> DeleteBasketAsync(string userId, CancellationToken cancellationToken = default)
            => inner.DeleteBasketAsync(userId, cancellationToken);

        public Task<bool> TryRemoveFromProductIndexAsync(
            Guid productId, string userId, ShoppingBasket? asRead, CancellationToken cancellationToken = default)
            => inner.TryRemoveFromProductIndexAsync(productId, userId, asRead, cancellationToken);

        public Task<IReadOnlyCollection<string>> GetUsersContainingProductAsync(Guid productId, CancellationToken cancellationToken = default)
            => inner.GetUsersContainingProductAsync(productId, cancellationToken);
    }
}
