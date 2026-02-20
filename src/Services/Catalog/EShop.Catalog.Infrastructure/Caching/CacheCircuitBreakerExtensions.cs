using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Infrastructure.Caching;

/// <summary>
/// Extension methods for adding circuit breaker cache decoration.
/// </summary>
public static class CacheCircuitBreakerExtensions
{
    /// <summary>
    /// Wraps the registered IDistributedCache with a circuit breaker decorator.
    /// Must be called AFTER AddStackExchangeRedisCache / AddDistributedMemoryCache.
    ///
    /// When Redis fails <paramref name="failureThreshold"/> times consecutively,
    /// all cache operations are skipped for <paramref name="openDuration"/>,
    /// preventing cascading 5-second timeout delays on every request.
    /// </summary>
    public static IServiceCollection AddCircuitBreakingCache(
        this IServiceCollection services,
        int failureThreshold = 3,
        TimeSpan? openDuration = null)
    {
        // Find the existing IDistributedCache registration
        var existingDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IDistributedCache));
        if (existingDescriptor == null)
            return services;

        // Remove the original registration
        services.Remove(existingDescriptor);

        // Re-register the original under a keyed/internal name
        services.Add(new ServiceDescriptor(
            typeof(InnerDistributedCacheMarker),
            sp =>
            {
                if (existingDescriptor.ImplementationFactory != null)
                    return new InnerDistributedCacheMarker((IDistributedCache)existingDescriptor.ImplementationFactory(sp));
                if (existingDescriptor.ImplementationInstance is IDistributedCache instance)
                    return new InnerDistributedCacheMarker(instance);
                if (existingDescriptor.ImplementationType != null)
                    return new InnerDistributedCacheMarker((IDistributedCache)ActivatorUtilities.CreateInstance(sp, existingDescriptor.ImplementationType));

                throw new InvalidOperationException("Cannot resolve inner IDistributedCache");
            },
            existingDescriptor.Lifetime));

        // Register the circuit-breaking decorator as IDistributedCache
        services.Add(new ServiceDescriptor(
            typeof(IDistributedCache),
            sp =>
            {
                var marker = sp.GetRequiredService<InnerDistributedCacheMarker>();
                var logger = sp.GetRequiredService<ILogger<CircuitBreakingDistributedCache>>();
                return new CircuitBreakingDistributedCache(
                    marker.Inner,
                    logger,
                    failureThreshold,
                    openDuration ?? TimeSpan.FromSeconds(30));
            },
            existingDescriptor.Lifetime));

        return services;
    }

    /// <summary>
    /// Internal marker to hold the original IDistributedCache instance.
    /// </summary>
    private class InnerDistributedCacheMarker
    {
        public IDistributedCache Inner { get; }
        public InnerDistributedCacheMarker(IDistributedCache inner) => Inner = inner;
    }
}
