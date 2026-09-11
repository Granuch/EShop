using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres host with a product repository whose SKU pre-check always answers "free", so a
/// duplicate SKU is guaranteed to reach <c>IX_Products_Sku</c>.
///
/// <para>
/// M15 (Catalog audit Stage 9). <c>ConcurrentOperationTests</c>' eight-way race accepts either a 400
/// (pre-check) or a 409 (index) for every loser, because which one a loser gets is timing — so it
/// cannot tell whether <c>AddProductSkuConflict()</c> maps the index violation to
/// <c>Product.SkuConflict</c>, or whether a race ever reached the index at all. Blinding the
/// pre-check makes the database path deterministic. Same approach as
/// <see cref="BlindSlugCheckApiFactory"/>: decorate one method, keep everything else real.
/// </para>
/// </summary>
public class BlindSkuCheckApiFactory : PostgresCatalogApiFactory
{
    private BlindSkuCheckApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static async Task<BlindSkuCheckApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        // Last registration wins for a single resolution, so this replaces the app's own.
        services.AddScoped<ProductRepository>();
        services.AddScoped<IProductRepository>(sp =>
            new BlindSkuCheckProductRepository(sp.GetRequiredService<ProductRepository>()));
    }

    private sealed class BlindSkuCheckProductRepository(IProductRepository inner) : IProductRepository
    {
        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.GetByIdAsync(id, cancellationToken);

        public Task<Product?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.GetByIdReadOnlyAsync(id, cancellationToken);

        public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> AnyInCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
            => inner.AnyInCategoryAsync(categoryId, cancellationToken);

        public Task AddAsync(Product product, CancellationToken cancellationToken = default)
            => inner.AddAsync(product, cancellationToken);

        public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(product, cancellationToken);

        public Task DeleteAsync(Product product, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(product, cancellationToken);
    }
}
