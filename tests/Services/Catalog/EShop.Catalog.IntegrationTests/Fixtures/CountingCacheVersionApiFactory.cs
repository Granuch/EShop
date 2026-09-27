using System.Collections.Concurrent;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres host with its <see cref="ICacheKeyVersionProvider"/> decorated to count family bumps (admin panel S16).
///
/// <para>
/// A bump is not observable in the cache itself — nothing is deleted, and <c>DistributedCacheKeyVersionProvider</c> writes
/// a fresh random value each time — so "bumped once, not once per product" can only be seen by counting the calls. The
/// real provider still runs underneath, so every other cache behaviour of the host is unchanged.
/// </para>
/// </summary>
public class CountingCacheVersionApiFactory : PostgresCatalogApiFactory
{
    private CountingCacheVersionApiFactory(string connectionString) : base(connectionString)
    {
    }

    /// <summary>Bumps per family since the host started. A singleton, so it sees every request scope.</summary>
    public ConcurrentDictionary<string, int> Bumps { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Admin panel S19. When set, a bump of the named family throws as an unreachable Redis would — before it is counted
    /// or reaches the real provider — so a test can see what an operator is told when the cache lever cannot be pulled.
    /// </summary>
    public string? FailingFamily { get; set; }

    public static async Task<CountingCacheVersionApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        // Last registration wins for a single resolution, so this replaces the app's own.
        services.AddScoped<DistributedCacheKeyVersionProvider>();
        services.AddScoped<ICacheKeyVersionProvider>(sp =>
            new CountingProvider(sp.GetRequiredService<DistributedCacheKeyVersionProvider>(), Bumps, () => FailingFamily));
    }

    private sealed class CountingProvider(
        ICacheKeyVersionProvider inner,
        ConcurrentDictionary<string, int> bumps,
        Func<string?> failingFamily)
        : ICacheKeyVersionProvider
    {
        public Task<string> GetVersionAsync(string family, CancellationToken cancellationToken = default)
            => inner.GetVersionAsync(family, cancellationToken);

        public Task BumpVersionAsync(string family, CancellationToken cancellationToken = default)
        {
            if (family == failingFamily())
                throw new InvalidOperationException("It was not possible to connect to the redis server(s): redis-secret-host:6379");

            bumps.AddOrUpdate(family, 1, (_, count) => count + 1);
            return inner.BumpVersionAsync(family, cancellationToken);
        }
    }
}
