using EShop.BuildingBlocks.Application.Caching;
using EShop.Ordering.Application.Orders;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Ordering.Infrastructure.Caching;

/// <summary>
/// Cache invalidation for <b>message consumers only</b>.
///
/// <para>
/// An HTTP command invalidates through <c>CacheInvalidationBehavior</c>, which runs after
/// <c>TransactionBehavior</c> has committed. A consumer's transaction belongs to
/// <c>IdempotentConsumer</c> and commits only after the handler returns, so the behavior would evict
/// before the write is visible and a concurrent read could re-cache the old state for the full TTL.
/// Consumers call this from <c>IdempotentConsumer.OnCommittedAsync</c> instead.
/// </para>
///
/// <para>
/// It may exist only because it builds keys exactly the way <c>CachingBehavior</c> does:
/// <see cref="CachingBehaviorOptions.StorageKeyFor"/> for the exact key and
/// <see cref="ICacheKeyVersionProvider"/> for the family. A hand-rolled key that differs by one
/// character evicts nothing and logs success — the pattern that made Ordering's list cache
/// uninvalidatable (audit H4). Do not call it from HTTP handlers; use the marker interface there.
/// </para>
/// </summary>
public sealed class OrderCacheInvalidator
{
    private readonly IDistributedCache _cache;
    private readonly ICacheKeyVersionProvider _versions;
    private readonly CachingBehaviorOptions _options;
    private readonly ILogger<OrderCacheInvalidator> _logger;

    public OrderCacheInvalidator(
        IDistributedCache cache,
        ICacheKeyVersionProvider versions,
        IOptions<CachingBehaviorOptions> options,
        ILogger<OrderCacheInvalidator> logger)
    {
        _cache = cache;
        _versions = versions;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Evicts the order's detail entry and every page of its owner's order list.</summary>
    public async Task InvalidateAsync(Guid orderId, string userId, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(_options.StorageKeyFor(OrderCacheKeys.Order(orderId)), cancellationToken);
            await _versions.BumpVersionAsync(OrderCacheKeys.UserOrders(userId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The write has already committed; a cache outage must not turn it into a failed message
            // and a redelivery. The cost is staleness for up to the entries' TTL.
            _logger.LogWarning(ex,
                "Cache invalidation failed for OrderId={OrderId}, UserId={UserId}", orderId, userId);
        }
    }
}
