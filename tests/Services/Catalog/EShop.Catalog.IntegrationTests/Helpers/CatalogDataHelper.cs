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

    public static async Task<Guid> CreateProductAsync(
        IServiceProvider services,
        string name,
        string sku,
        decimal price,
        int stockQuantity,
        Guid categoryId)
    {
        var db = services.GetRequiredService<CatalogDbContext>();
        var product = Product.Create(name, sku, price, stockQuantity, categoryId);
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
            await db.Products.AddAsync(product);
            ids.Add(product.Id);
        }

        await db.SaveChangesAsync();
        return ids;
    }
}
