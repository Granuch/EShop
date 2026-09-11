using EShop.BuildingBlocks.Application.Caching;
using EShop.Ordering.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrderEntity = EShop.Ordering.Domain.Entities.Order;

namespace EShop.Ordering.UnitTests.Consumers;

/// <summary>
/// A real <see cref="OrderCacheInvalidator"/> over mocked cache and version store, configured with
/// Ordering's production key prefix and version. The assertions spell out the literal keys on purpose:
/// the bug this guards (audit H4) was a key that differed from the cached one and evicted nothing.
/// </summary>
internal sealed class CacheSpy
{
    public Mock<IDistributedCache> Cache { get; } = new();
    public Mock<ICacheKeyVersionProvider> Versions { get; } = new();

    public OrderCacheInvalidator Invalidator => new(
        Cache.Object,
        Versions.Object,
        Options.Create(new CachingBehaviorOptions { KeyPrefix = "ordering:", Version = "v1", UseVersioning = true }),
        NullLogger<OrderCacheInvalidator>.Instance);

    public void VerifyInvalidated(OrderEntity order)
    {
        Cache.Verify(x => x.RemoveAsync($"ordering:v1:order:{order.Id}", It.IsAny<CancellationToken>()), Times.Once);
        Versions.Verify(x => x.BumpVersionAsync($"orders:user:{order.UserId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    public void VerifyNothingInvalidated()
    {
        Cache.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Versions.Verify(x => x.BumpVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
