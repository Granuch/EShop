using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Behaviors;
using EShop.BuildingBlocks.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Extensions;

/// <summary>
/// Registers <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/> and the scoped
/// <see cref="ICacheInvalidationContext"/> it drains.
/// </summary>
public static class CacheInvalidationServiceCollectionExtensions
{
    /// <summary>
    /// <b>Call this FIRST in <c>Program.cs</c> — before <c>Add&lt;Service&gt;Application()</c> and
    /// <c>Add&lt;Service&gt;Infrastructure()</c>. The position is the entire reason this method
    /// exists</b>, and moving the call is a silent behaviour change, not a style choice.
    ///
    /// <para>
    /// MediatR runs <c>IPipelineBehavior</c> in DI registration order, first registered outermost.
    /// This behavior used to live in each service's Infrastructure registration, which put it
    /// <i>inside</i> <c>TransactionBehavior</c> — so it evicted keys and bumped family versions
    /// while the write was still uncommitted. A concurrent read in that window repopulated the
    /// cache with pre-commit data under the freshly bumped version, where it survived the full
    /// TTL: the bump meant to fix staleness was what made the stale entry addressable. All four
    /// services carrying these behaviors had it — Basket, Catalog, Identity and Ordering — because
    /// their registration order was already uniform, so this was one shared defect rather than
    /// four bugs.
    /// </para>
    ///
    /// <para>
    /// Registering here yields
    /// <c>CacheInvalidation → Transaction → Validation → Logging → Caching → handler</c>.
    /// <c>CachingBehavior</c> deliberately stays in each service's Infrastructure registration:
    /// queries are not transactional, so its position is immaterial, and it needs
    /// <c>IDistributedCache</c> wiring that is configured per service. The hazard that Stage 7's
    /// reordering fixed — validation running after the transaction had opened — stays fixed,
    /// because Transaction/Validation/Logging keep their relative order.
    /// </para>
    ///
    /// <para>
    /// Do not "simplify" this back into <c>Add&lt;Service&gt;Infrastructure()</c>. Infrastructure
    /// is registered after Application in every service, so any registration made there is inside
    /// the transaction by construction — which is exactly the defect.
    /// </para>
    /// </summary>
    public static IServiceCollection AddEShopCacheInvalidation(this IServiceCollection services)
    {
        services.AddScoped<ICacheInvalidationContext, CacheInvalidationContext>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));

        return services;
    }
}
