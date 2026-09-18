using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Fixtures;

/// <summary>
/// The Postgres host with a product repository that loads a product <b>without</b> its attributes,
/// so <c>Product.AddAttribute</c>'s in-memory dedupe sees an empty collection and always passes —
/// which guarantees a duplicate name reaches M1's unique index
/// <c>IX_ProductAttributes_ProductId_Name</c>.
/// </summary>
/// <remarks>
/// <para>
/// M1 / A6 (Admin panel S3). The real race — two requests that each load the product before the
/// other's attribute exists — is reachable through ordinary concurrency, but which of the two
/// answers a 400 (the aggregate saw it) and which a 409 (the index did) is timing. A test that
/// accepts either cannot tell whether the index exists at all, and this repo has already been
/// bitten by a "concurrency test" that never actually raced. Blinding the pre-check makes the
/// database path deterministic instead.
/// </para>
/// <para>
/// Same approach as <see cref="BlindSkuCheckApiFactory"/> and <see cref="BlindSlugCheckApiFactory"/>:
/// change one method, keep everything else real. Here the change is the <c>Include</c> rather than
/// a return value, because the check being blinded lives in the aggregate and can only be reached
/// through what was loaded into it.
/// </para>
/// </remarks>
public class BlindAttributeCheckApiFactory : PostgresCatalogApiFactory
{
    private BlindAttributeCheckApiFactory(string connectionString) : base(connectionString)
    {
    }

    public static async Task<BlindAttributeCheckApiFactory> CreateAsync(CancellationToken cancellationToken = default)
        => new(await PostgresTestServer.CreateDatabaseAsync(cancellationToken));

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        // Last registration wins for a single resolution, so this replaces the app's own.
        services.AddScoped<ProductRepository>();
        services.AddScoped<IProductRepository>(sp =>
            new BlindAttributeCheckProductRepository(
                sp.GetRequiredService<ProductRepository>(),
                sp.GetRequiredService<CatalogDbContext>()));
    }

    private sealed class BlindAttributeCheckProductRepository(IProductRepository inner, CatalogDbContext context)
        : IProductRepository
    {
        /// <summary>
        /// The only altered method: identical to <c>ProductRepository.GetByIdAsync</c> except that
        /// <c>Attributes</c> is not included, so the tracked product's attribute collection is
        /// empty whatever is stored.
        /// </summary>
        public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => await context.Products
                .Include(p => p.Category)
                .Include(p => p.Images)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        public Task<Product?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.GetByIdReadOnlyAsync(id, cancellationToken);

        public Task<bool> SkuExistsAsync(string sku, Guid? excludingProductId = null, CancellationToken cancellationToken = default)
            => inner.SkuExistsAsync(sku, excludingProductId, cancellationToken);

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
