using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Fixtures;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// M15 (Catalog audit Stage 9), the database side of SKU uniqueness. With the handler's pre-check
/// blinded (<see cref="BlindSkuCheckApiFactory"/>), only the partial unique index
/// <c>IX_Products_Sku</c> stands between a request and a duplicate SKU — so these pin what the index
/// enforces and how a violation is reported, independently of the pre-check that normally answers
/// first and hides both.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class ProductSkuConflictTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    protected override async Task<CatalogApiFactory> CreateFactoryAsync()
        => await BlindSkuCheckApiFactory.CreateAsync();

    private async Task<HttpResponseMessage> PostAsync(string name, string sku)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        return await Client.PostAsJsonAsync(ProductsEndpoint, new CreateProductRequest
        {
            Name = name,
            Sku = sku,
            Price = 10m,
            StockQuantity = 1,
            CategoryId = categoryId
        });
    }

    private async Task<Guid> CreateAsync(string name, string sku)
    {
        using var response = await PostAsync(name, sku);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    [Test]
    public async Task ADuplicateLiveSkuReachingTheIndex_IsA409SkuConflict()
    {
        var sku = CatalogDataHelper.GenerateUniqueSku("IDX");
        await CreateAsync("First", sku);

        using var response = await PostAsync("Second", sku);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Product.SkuConflict",
            "AddProductSkuConflict must claim the violation before AddEfDuplicateKey reports a generic DuplicateResource");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await db.Products.CountAsync(p => p.Sku == sku)).Should().Be(1, "the rejected write must not have persisted");
    }

    /// <summary>
    /// D2. With the pre-check blinded, only the index decides — so this passes only because
    /// <c>IX_Products_Sku</c> is partial (<c>WHERE NOT "IsDeleted"</c>). A total unique index would
    /// keep the discontinued product's SKU forever.
    /// </summary>
    [Test]
    public async Task ASoftDeletedProductsSku_IsAcceptedByTheIndex()
    {
        var sku = CatalogDataHelper.GenerateUniqueSku("IDXREUSE");
        var id = await CreateAsync("Discontinued", sku);
        (await Client.DeleteAsync($"{ProductsEndpoint}/{id}")).IsSuccessStatusCode.Should().BeTrue();

        using var response = await PostAsync("Replacement", sku);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
