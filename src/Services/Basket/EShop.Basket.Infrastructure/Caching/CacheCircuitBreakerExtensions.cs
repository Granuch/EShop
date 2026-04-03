using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Infrastructure.Caching;

public static class CacheCircuitBreakerExtensions
{
    public static IServiceCollection AddCircuitBreakingCache(
        this IServiceCollection services,
        int failureThreshold = 3,
        TimeSpan? openDuration = null)
    {
        var existingDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IDistributedCache));
        if (existingDescriptor == null)
            return services;

        services.Remove(existingDescriptor);

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

    private sealed class InnerDistributedCacheMarker
    {
        public IDistributedCache Inner { get; }

        public InnerDistributedCacheMarker(IDistributedCache inner)
        {
            Inner = inner;
        }
    }
}
