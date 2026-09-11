using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres host with a category repository whose slug pre-check always answers "free", so a
/// duplicate slug is guaranteed to reach the unique index.
///
/// <para>
/// This is how the database-side half of M9 is tested deterministically. Racing concurrent requests
/// past the real pre-check does not work: Stage 8's falsification removed
/// <c>AddCategorySlugConflict()</c> entirely and the concurrent-create test stayed green, because the
/// pre-check answered every loser before any of them reached the index. Same approach as Identity's
/// <c>FailingRevokeApiFactory</c> — decorate one method, keep everything else real.
/// </para>
/// </summary>
public class BlindSlugCheckApiFactory : PostgresCatalogApiFactory
{
    private BlindSlugCheckApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static async Task<BlindSlugCheckApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        // Last registration wins for a single resolution, so this replaces the app's own.
        services.AddScoped<CategoryRepository>();
        services.AddScoped<ICategoryRepository>(sp =>
            new BlindSlugCheckCategoryRepository(sp.GetRequiredService<CategoryRepository>()));
    }

    private sealed class BlindSlugCheckCategoryRepository(ICategoryRepository inner) : ICategoryRepository
    {
        public Task<Category?> GetById(Guid id, CancellationToken cancellationToken = default)
            => inner.GetById(id, cancellationToken);

        public Task AddAsync(Category category, CancellationToken cancellationToken = default)
            => inner.AddAsync(category, cancellationToken);

        public Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(category, cancellationToken);

        public Task<List<Category>> GetRootCategories(CancellationToken cancellationToken = default)
            => inner.GetRootCategories(cancellationToken);

        public Task<bool> SlugExistsAsync(Guid? parentCategoryId, string slug, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
