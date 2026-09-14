using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// DEBT-16, end to end. A product write must be visible in the next list read.
///
/// <para>
/// The <c>products:list:*</c> cache keys embed ten filter/sort/page parameters, so the set of live
/// keys is unbounded and <c>ICacheInvalidatingCommand</c> — which removes exact keys only — could
/// never name them. List results therefore stayed stale for the full 5-minute TTL after any write,
/// and that fact had been copy-pasted as a comment into four command handlers rather than fixed.
/// Creation was the worst case: a newly created product did not appear in any cached list, so the
/// POST looked as if it had silently failed.
/// </para>
///
/// <para>
/// These tests go through HTTP precisely because that is what exercises <c>CachingBehavior</c>. A
/// handler-level test would bypass the cache entirely and pass no matter what — which is the trap
/// that let the original bug persist while the suite stayed green.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductListCacheInvalidationTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    // Cached separately per parameter set, so the assertions below must reuse one exact query.
    private const string ListQuery = ProductsEndpoint + "?PageNumber=1&PageSize=50";

    private async Task<Guid> CategoryIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
    }

    private async Task<List<ProductResponse>> ListAsync()
    {
        var response = await Client.GetAsync(ListQuery);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        return page!.Items.ToList();
    }

    [Test]
    public async Task CreatingAProduct_MakesItVisibleInAnAlreadyCachedList()
    {
        // Populate the cache for this exact key.
        var before = await ListAsync();

        var sku = CatalogDataHelper.GenerateUniqueSku("CACHE");
        var create = new CreateProductRequest
        {
            Name = "Cache Invalidation Probe",
            Sku = sku,
            Price = 19.99m,
            StockQuantity = 5,
            CategoryId = await CategoryIdAsync()
        };

        var created = await Client.PostAsJsonAsync(ProductsEndpoint, create);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await ListAsync();

        after.Should().Contain(p => p.Sku == sku,
            "the list read after the write must not be served from the pre-write cache entry");
        after.Count.Should().BeGreaterThan(before.Count);
    }

    [Test]
    public async Task UpdatingAProduct_IsReflectedInAnAlreadyCachedList()
    {
        var categoryId = await CategoryIdAsync();
        var sku = CatalogDataHelper.GenerateUniqueSku("UPD");

        var create = await Client.PostAsJsonAsync(ProductsEndpoint, new CreateProductRequest
        {
            Name = "Before Update",
            Sku = sku,
            Price = 10.00m,
            StockQuantity = 5,
            CategoryId = categoryId
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var productId = (await create.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        // Populate the cache with the pre-update price.
        (await ListAsync()).Should().Contain(p => p.Sku == sku && p.Price == 10.00m);

        // ProductId is required in the body as well as the route — see UpdateProductTests.
        var update = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 42.00m,
            StockQuantity = 5
        });
        update.IsSuccessStatusCode.Should().BeTrue();

        var after = await ListAsync();

        after.Should().Contain(p => p.Sku == sku && p.Price == 42.00m,
            "a cached list must not keep serving the pre-update price");
    }
}
