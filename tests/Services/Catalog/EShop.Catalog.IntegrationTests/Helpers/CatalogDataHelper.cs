using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Helpers;

/// <summary>
/// Helper methods for managing test data
/// </summary>
public static class CatalogDataHelper
{
    /// <summary>
    /// Generates a unique SKU with a prefix. Result is always ≤ 50 characters.
    /// </summary>
    public static string GenerateUniqueSku(string prefix = "TST")
        => $"{prefix}-{Guid.NewGuid():N}";

    public static async Task<Guid> CreateCategoryAsync(
        IServiceProvider services,
        string name,
        string? slug = null,
        Guid? parentCategoryId = null)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var category = Category.Create(name, slug, parentCategoryId);
        await db.Categories.AddAsync(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>
    /// Creates a product and, by default, <b>publishes</b> it.
    ///
    /// <para>
    /// D1 / H5a: <c>Product.Create</c> yields <c>ProductStatus.Draft</c>, and the public read paths
    /// now return published products only. A test that seeds a draft and then asserts it appears in
    /// an anonymous list is asserting the bug, so publishing is the useful default — pass
    /// <paramref name="publish"/> = false when the draft state is the point of the test.
    /// </para>
    /// </summary>
    public static async Task<Guid> CreateProductAsync(
        IServiceProvider services,
        string name,
        string sku,
        decimal price,
        int stockQuantity,
        Guid categoryId,
        bool publish = true)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var product = Product.Create(name, sku, price, stockQuantity, categoryId);
        if (publish)
        {
            product.Publish();
        }

        await db.Products.AddAsync(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    public static async Task<Guid> GetFirstCategoryIdAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var category = db.Categories.FirstOrDefault();
        if (category == null)
        {
            return await CreateCategoryAsync(services, "Test Category", "test-category");
        }
        return category.Id;
    }

    public static async Task<List<Guid>> CreateBulkProductsAsync(
        IServiceProvider services,
        int count,
        Guid categoryId)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var ids = new List<Guid>();

        for (int i = 0; i < count; i++)
        {
            var product = Product.Create(
                $"Bulk Product {i}",
                GenerateUniqueSku($"BLK{i}"),
                10m + i,
                i * 10,
                categoryId);
            // Published, like CreateProductAsync — bulk rows exist to be found by list queries.
            product.Publish();
            await db.Products.AddAsync(product);
            ids.Add(product.Id);
        }

        await db.SaveChangesAsync();
        return ids;
    }
}
