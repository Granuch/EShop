using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Admin panel S4 — product lifecycle (restore, stock adjustment) and the admin reads that make it
/// usable: the deleted-products recycle bin, the low-stock dashboard, and the new list filters.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductLifecycleAndAdminReadsTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";
    private const string LowStockEndpoint = "/api/v1/admin/catalog/low-stock";

    private async Task<(Guid ProductId, Guid CategoryId, string Sku)> SeedAsync(
        string name = "S4 product", int stock = 10, bool publish = true)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var sku = CatalogDataHelper.GenerateUniqueSku("S4");
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, name, sku, 20m, stock, categoryId, publish: publish);
        return (productId, categoryId, sku);
    }

    private async Task<Product?> StoredAsync(Guid productId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Products.AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.Id == productId);
    }

    #region Restore

    [Test]
    public async Task Restore_BringsADeletedProductBack_AsDraft()
    {
        var (productId, _, _) = await SeedAsync();
        (await Client.DeleteAsync($"{ProductsEndpoint}/{productId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        // The deleted product is invisible even to an admin's detail read — the global query filter
        // applies to every path except the ones that explicitly lift it.
        (await Client.GetAsync($"{ProductsEndpoint}/{productId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var response = await Client.PostAsync($"{ProductsEndpoint}/{productId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var stored = await StoredAsync(productId);
        stored!.IsDeleted.Should().BeFalse();
        stored.DeletedAt.Should().BeNull();
        stored.Status.Should().Be(ProductStatus.Draft,
            "restoring must never silently put a product back in front of customers");
    }

    [Test]
    public async Task ARestoredProduct_IsNotInThePublicCatalogueUntilRepublished()
    {
        // The practical consequence of restoring to Draft, asserted through the anonymous view.
        var (productId, _, sku) = await SeedAsync();
        await Client.DeleteAsync($"{ProductsEndpoint}/{productId}");
        await Client.PostAsync($"{ProductsEndpoint}/{productId}/restore", null);

        // A second client, not Headers.Authorization = null: HttpClient merges DefaultRequestHeaders
        // into any request that lacks them, so nulling the header leaves the bearer token in place
        // and the test silently asserts the authenticated view instead of the public one.
        using var anonymous = Factory.CreateClient();
        var list = await anonymous.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageNumber=1&PageSize=100&SearchTerm={sku}");
        list!.Items.Should().NotContain(p => p.Id == productId);

        (await Client.PostAsync($"{ProductsEndpoint}/{productId}/publish", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var afterPublish = await anonymous.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageNumber=1&PageSize=100&SearchTerm={sku}");
        afterPublish!.Items.Should().Contain(p => p.Id == productId);
    }

    [Test]
    public async Task Restore_WhenAnotherProductTookTheSku_Is409_AndChangesNothing()
    {
        // A3. IX_Products_Sku is unique filtered NOT "IsDeleted", so deleting frees the SKU and
        // restoring re-enters the index. Without the handler's pre-check this is a raw 23505.
        var (productId, categoryId, sku) = await SeedAsync();
        await Client.DeleteAsync($"{ProductsEndpoint}/{productId}");

        using (var scope = Factory.Services.CreateScope())
        {
            await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Took the sku", sku, 30m, 1, categoryId);
        }

        var response = await Client.PostAsync($"{ProductsEndpoint}/{productId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the request is fine — it is another product's state that blocks it, and retrying after "
            + "resolving that will succeed");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Product.SkuConflict");

        // Asserting the detail, not just the code, is what makes this test about the handler's
        // pre-check. AddProductSkuConflict() maps the IX_Products_Sku violation to the SAME 409 and
        // the SAME errorCode, so deleting the pre-check leaves status and code unchanged and a
        // weaker assertion passes either way — the "two redundant lines" shape. The detail is the
        // only place the two paths differ: the pre-check names the conflict and tells the admin what
        // to do, while the index branch describes a concurrent create and says to retry, which would
        // be actively misleading advice here since retrying unchanged can never succeed.
        problem.Detail.Should().Contain("already used by another product",
            "this must be answered by the handler's pre-check, not by the unique index's race backstop");

        (await StoredAsync(productId))!.IsDeleted.Should().BeTrue("a refused restore must change nothing");
    }

    [Test]
    public async Task Restore_OfALiveProduct_Is400()
    {
        var (productId, _, _) = await SeedAsync();

        var response = await Client.PostAsync($"{ProductsEndpoint}/{productId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Product.NotDeleted");
    }

    [Test]
    public async Task Restore_OfAnUnknownProduct_Is404()
    {
        var response = await Client.PostAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Product.NotFound");
    }

    #endregion

    #region PATCH stock

    [Test]
    public async Task AdjustStock_WithADelta_MovesStock_AndReturnsTheNewQuantity()
    {
        var (productId, _, _) = await SeedAsync(stock: 10);

        var response = await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{productId}/stock",
            new AdjustProductStockRequest { Delta = 15, Reason = "Delivery" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ProductStockResponse>();
        body!.ProductId.Should().Be(productId);
        body.StockQuantity.Should().Be(25);

        (await StoredAsync(productId))!.StockQuantity.Should().Be(25);
    }

    [Test]
    public async Task AdjustStock_DoesNotTouchPrice()
    {
        // The reason this endpoint exists: the only previous way to change stock was the full PUT,
        // which also rewrites price — so a stale admin form reverted someone else's price change.
        var (productId, _, _) = await SeedAsync(stock: 10);

        await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{productId}/stock",
            new AdjustProductStockRequest { Delta = 1 });

        (await StoredAsync(productId))!.Price.Should().Be(20m);
    }

    [Test]
    public async Task AdjustStock_WithAnAbsolute_SetsIt()
    {
        var (productId, _, _) = await SeedAsync(stock: 10);

        var response = await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{productId}/stock",
            new AdjustProductStockRequest { Absolute = 3, Reason = "Stock-take" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ProductStockResponse>())!.StockQuantity.Should().Be(3);
    }

    [Test]
    public async Task AdjustStock_BelowZero_Is400_AndChangesNothing()
    {
        var (productId, _, _) = await SeedAsync(stock: 4);

        var response = await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{productId}/stock",
            new AdjustProductStockRequest { Delta = -5 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredAsync(productId))!.StockQuantity.Should().Be(4);
    }

    [Test]
    public async Task AdjustStock_WithNeitherDeltaNorAbsolute_Is400()
    {
        var (productId, _, _) = await SeedAsync();

        var response = await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{productId}/stock",
            new AdjustProductStockRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AdjustStock_WithBothDeltaAndAbsolute_Is400()
    {
        // Rejected rather than resolved by precedence: answering "delta wins" silently is the worse
        // outcome, because the caller sent both precisely because they were unsure.
        var (productId, _, _) = await SeedAsync(stock: 10);

        var response = await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{productId}/stock",
            new AdjustProductStockRequest { Delta = 1, Absolute = 50 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredAsync(productId))!.StockQuantity.Should().Be(10);
    }

    [Test]
    public async Task AdjustStock_OnAnUnknownProduct_Is404()
    {
        var response = await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/stock",
            new AdjustProductStockRequest { Delta = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AdjustStock_InvalidatesTheCachedDetail()
    {
        var (productId, _, _) = await SeedAsync(stock: 10);
        (await Client.GetFromJsonAsync<ProductDetailsResponse>($"{ProductsEndpoint}/{productId}"))!
            .StockQuantity.Should().Be(10);

        await Client.PatchAsJsonAsync($"{ProductsEndpoint}/{productId}/stock",
            new AdjustProductStockRequest { Delta = 5 });

        (await Client.GetFromJsonAsync<ProductDetailsResponse>($"{ProductsEndpoint}/{productId}"))!
            .StockQuantity.Should().Be(15);
    }

    #endregion

    #region Admin reads

    [Test]
    public async Task DeletedProducts_ListsOnlyDeletedOnes()
    {
        var (deletedId, _, _) = await SeedAsync("To be deleted");
        var (liveId, _, _) = await SeedAsync("Stays live");
        await Client.DeleteAsync($"{ProductsEndpoint}/{deletedId}");

        var list = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}/deleted?PageNumber=1&PageSize=100");

        list!.Items.Should().Contain(p => p.Id == deletedId);
        list.Items.Should().NotContain(p => p.Id == liveId,
            "IgnoreQueryFilters lifts the filter for the WHOLE query — the explicit IsDeleted "
            + "predicate is what scopes this to the bin rather than to every product");
    }

    [Test]
    public async Task DeletedProducts_RefusesAnonymous()
    {
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync($"{ProductsEndpoint}/deleted");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ARestoredProduct_LeavesTheDeletedList()
    {
        var (productId, _, _) = await SeedAsync();
        await Client.DeleteAsync($"{ProductsEndpoint}/{productId}");
        await Client.PostAsync($"{ProductsEndpoint}/{productId}/restore", null);

        var list = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}/deleted?PageNumber=1&PageSize=100");

        list!.Items.Should().NotContain(p => p.Id == productId,
            "the bin is deliberately uncached, so a restore is visible on the next reload");
    }

    [Test]
    public async Task LowStock_ListsProductsBelowTheThreshold_Only()
    {
        var (lowId, _, _) = await SeedAsync("Running out", stock: 2);
        var (highId, _, _) = await SeedAsync("Plenty", stock: 500);

        var list = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{LowStockEndpoint}?threshold=5&PageSize=100");

        list!.Items.Should().Contain(p => p.Id == lowId);
        list.Items.Should().NotContain(p => p.Id == highId);
    }

    [Test]
    public async Task LowStock_IsStrictlyBelow()
    {
        var (exactlyAtId, _, _) = await SeedAsync("Exactly at threshold", stock: 5);

        var list = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{LowStockEndpoint}?threshold=5&PageSize=100");

        list!.Items.Should().NotContain(p => p.Id == exactlyAtId,
            "strictly-below is what makes threshold=1 mean 'out of stock'");
    }

    [Test]
    public async Task LowStock_IncludesDraftProducts()
    {
        // An admin read: a draft that is out of stock is exactly what needs seeing before publish.
        var (draftId, _, _) = await SeedAsync("Draft and empty", stock: 0, publish: false);

        var list = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{LowStockEndpoint}?threshold=1&PageSize=100");

        list!.Items.Should().Contain(p => p.Id == draftId);
    }

    [Test]
    public async Task LowStock_RefusesAnonymous()
    {
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync($"{LowStockEndpoint}?threshold=5");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task LowStock_WithAZeroThreshold_Is400()
    {
        var response = await Client.GetAsync($"{LowStockEndpoint}?threshold=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a zero threshold is an always-empty page, which is a caller error rather than a request");
    }

    #endregion

    #region New list filters

    [Test]
    public async Task ListFilter_StockBelow_NarrowsTheResultSet()
    {
        // Unique SearchTerm for cache isolation — see the comment in
        // ListFilters_AreInTheCacheKey_SoOneDoesNotServeAnother. Without it this test can be served
        // another test's cached page for the identical URL.
        var token = $"SB{Guid.NewGuid():N}";
        var (lowId, _, _) = await SeedAsync($"{token} low", stock: 1);
        var (highId, _, _) = await SeedAsync($"{token} high", stock: 900);

        var filtered = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={token}&StockBelow=5");

        filtered!.Items.Should().Contain(p => p.Id == lowId);
        filtered.Items.Should().NotContain(p => p.Id == highId);
    }

    [Test]
    public async Task ListFilter_HasDiscount_NarrowsTheResultSet()
    {
        var token = $"HD{Guid.NewGuid():N}";
        var (discountedId, _, _) = await SeedAsync($"{token} discounted");
        var (plainId, _, _) = await SeedAsync($"{token} plain");
        (await Client.PutAsJsonAsync($"{ProductsEndpoint}/{discountedId}/discount",
            new SetProductDiscountRequest { DiscountPrice = 5m })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var discounted = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={token}&HasDiscount=true");
        discounted!.Items.Should().Contain(p => p.Id == discountedId);
        discounted.Items.Should().NotContain(p => p.Id == plainId);

        var undiscounted = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={token}&HasDiscount=false");
        undiscounted!.Items.Should().Contain(p => p.Id == plainId);
        undiscounted.Items.Should().NotContain(p => p.Id == discountedId);
    }

    [Test]
    public async Task ListFilter_Status_NarrowsTheResultSet_ForAnAdmin()
    {
        var token = $"ST{Guid.NewGuid():N}";
        var (draftId, _, _) = await SeedAsync($"{token} draft", publish: false);
        var (activeId, _, _) = await SeedAsync($"{token} active");

        var drafts = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={token}&Status=Draft");

        drafts!.Items.Should().Contain(p => p.Id == draftId);
        drafts.Items.Should().NotContain(p => p.Id == activeId);
    }

    [Test]
    public async Task ListFilter_Status_CannotBeUsedToReadDraftsAnonymously()
    {
        // The security-shaped half of the Status filter: it ANDs with the published-only rule
        // rather than replacing it, so a public caller asking for Draft gets nothing — not the
        // unpublished catalogue.
        var (draftId, _, sku) = await SeedAsync("Anonymous draft probe", publish: false);

        using var anonymous = Factory.CreateClient();
        var list = await anonymous.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&Status=Draft&SearchTerm={sku}");

        list!.Items.Should().BeEmpty();
        list.Items.Should().NotContain(p => p.Id == draftId);
    }

    [Test]
    public async Task ListFilter_CreatedFromAndTo_NarrowTheResultSet()
    {
        var (productId, _, sku) = await SeedAsync("Created range filter");

        var inRange = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={sku}&CreatedFrom={DateTime.UtcNow.AddDays(-1):O}");
        inRange!.Items.Should().Contain(p => p.Id == productId);

        var outOfRange = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={sku}&CreatedTo={DateTime.UtcNow.AddDays(-1):O}");
        outOfRange!.Items.Should().NotContain(p => p.Id == productId);
    }

    [Test]
    public async Task ListFilters_AreInTheCacheKey_SoOneDoesNotServeAnother()
    {
        // The trap this stage had to avoid: a filter that narrows the result set but is absent from
        // CacheKey makes two different requests share one entry. Priming the unfiltered page first
        // is what would poison the filtered one.
        //
        // The unique SearchTerm is load-bearing, not decoration. SearchTerm is already part of
        // CacheKey, so it gives this test its own pair of cache entries — without it the URLs are
        // byte-identical to the ones ListFilter_StockBelow_NarrowsTheResultSet already primed, and
        // since PERF-02 gives the whole fixture one host and one Redis, this test would assert
        // against that earlier test's cached page instead of its own data.
        var token = $"CK{Guid.NewGuid():N}";
        var (lowId, _, _) = await SeedAsync($"{token} low", stock: 1);
        var (highId, _, _) = await SeedAsync($"{token} high", stock: 900);

        var unfiltered = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={token}");
        unfiltered!.Items.Should().Contain(p => p.Id == highId);

        var filtered = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}?PageSize=100&SearchTerm={token}&StockBelow=5");

        filtered!.Items.Should().NotContain(p => p.Id == highId,
            "the filtered request must not be served the cached unfiltered page");
        filtered.Items.Should().Contain(p => p.Id == lowId);
    }

    [Test]
    public async Task ListFilters_AreOptional_SoOmittingThemStillBinds()
    {
        // [AsParameters] makes a non-nullable value type a REQUIRED query-string parameter, and the
        // resulting 400 reads "The request body is not valid JSON" on a GET with no body.
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
