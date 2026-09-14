using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Categories;

/// <summary>
/// D5: <c>GET /api/v1/categories/{id}/products</c> is paged, and it is no longer capped at 200.
///
/// <para>
/// The cache tests matter as much as the paging ones. The per-category list used to be evicted by
/// an exact key, <c>products:category:{id}</c>, from ten product handlers. Paged keys cannot be
/// named that way, so the query moved into the versioned <c>products:list</c> family and those
/// handler calls were deleted — which makes the family membership the only thing keeping a cached
/// page from outliving a write. These go through HTTP because that is what exercises
/// <c>CachingBehavior</c>; a handler-level test would pass with no cache at all.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CategoryProductsPaginationTests : AuthenticatedIntegrationTestBase
{
    private const string CategoriesEndpoint = "/api/v1/categories";
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<Guid> NewCategoryAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Paged Category", $"paged-{Guid.NewGuid():N}");
    }

    private async Task<Guid> NewProductAsync(Guid categoryId, bool publish = true)
    {
        using var scope = Factory.Services.CreateScope();
        return await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Paged Product", CatalogDataHelper.GenerateUniqueSku("PAGE"),
            10m, 5, categoryId, publish);
    }

    private async Task<PagedResponse<ProductResponse>> PageAsync(Guid categoryId, int pageNumber, int pageSize)
    {
        var response = await Client.GetAsync(
            $"{CategoriesEndpoint}/{categoryId}/products?PageNumber={pageNumber}&PageSize={pageSize}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>())!;
    }

    [Test]
    public async Task Products_AreServedInPages_WithAnAccurateTotal()
    {
        var categoryId = await NewCategoryAsync();
        var created = new[]
        {
            await NewProductAsync(categoryId),
            await NewProductAsync(categoryId),
            await NewProductAsync(categoryId)
        };

        var first = await PageAsync(categoryId, 1, 2);
        var second = await PageAsync(categoryId, 2, 2);

        first.TotalCount.Should().Be(3);
        first.TotalPages.Should().Be(2);
        first.Items.Should().HaveCount(2);
        first.HasNextPage.Should().BeTrue();

        second.Items.Should().HaveCount(1);
        second.HasNextPage.Should().BeFalse();

        // Same name on every row, so only the Id tiebreaker keeps the two pages disjoint.
        first.Items.Concat(second.Items).Select(p => p.Id).Should().BeEquivalentTo(created);
    }

    [Test]
    public async Task MoreThan200Products_AreAllReachable()
    {
        // The retired cap: this used to answer with exactly 200 rows and nothing to say that the
        // 201st existed.
        var categoryId = await NewCategoryAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            await CatalogDataHelper.CreateBulkProductsAsync(scope.ServiceProvider, 201, categoryId);
        }

        var last = await PageAsync(categoryId, 3, 100);

        last.TotalCount.Should().Be(201);
        last.Items.Should().HaveCount(1);
    }

    [TestCase(0)]
    [TestCase(101)]
    public async Task AnOutOfRangePageSize_Is400_NotAMissingCategory(int pageSize)
    {
        // The endpoint used to map every error to 404, so a validation failure would have read as
        // "no such category".
        var categoryId = await NewCategoryAsync();

        var response = await Client.GetAsync($"{CategoriesEndpoint}/{categoryId}/products?PageSize={pageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Validation.Failed");
    }

    [Test]
    public async Task DraftsAreExcluded_EvenForAnAdmin()
    {
        // Client is admin-authenticated. This endpoint stays published-only for every caller — see
        // GetProductByCategoryQuery for why that is now a decision rather than a constraint.
        var categoryId = await NewCategoryAsync();
        var published = await NewProductAsync(categoryId);
        await NewProductAsync(categoryId, publish: false);

        var page = await PageAsync(categoryId, 1, 10);

        page.Items.Select(p => p.Id).Should().Equal(published);
        page.TotalCount.Should().Be(1, "the count must describe what the caller can reach");
    }

    [Test]
    public async Task PublishingAProduct_MakesItVisibleInAnAlreadyCachedPage()
    {
        var categoryId = await NewCategoryAsync();
        var draft = await NewProductAsync(categoryId, publish: false);

        // Populate the cache for this exact page.
        (await PageAsync(categoryId, 1, 10)).Items.Should().BeEmpty();

        var publish = await Client.PostAsync($"{ProductsEndpoint}/{draft}/publish", null);
        publish.IsSuccessStatusCode.Should().BeTrue();

        (await PageAsync(categoryId, 1, 10)).Items.Select(p => p.Id).Should().Equal(
            [draft], "a cached category page must not keep serving the pre-publish result");
    }

    [Test]
    public async Task DeletingAProduct_RemovesItFromAnAlreadyCachedPage()
    {
        var categoryId = await NewCategoryAsync();
        var product = await NewProductAsync(categoryId);

        (await PageAsync(categoryId, 1, 10)).Items.Select(p => p.Id).Should().Equal(product);

        var delete = await Client.DeleteAsync($"{ProductsEndpoint}/{product}");
        delete.IsSuccessStatusCode.Should().BeTrue();

        (await PageAsync(categoryId, 1, 10)).Items.Should().BeEmpty(
            "a cached category page must not keep serving a deleted product");
    }
}
