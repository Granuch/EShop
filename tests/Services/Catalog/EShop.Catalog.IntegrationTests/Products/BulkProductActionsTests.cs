using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Admin panel S16, endpoints #42–46. Each bulk action on real Postgres: what it stores, what it reports per product,
/// what it refuses as a whole, and that the caches it has to clear are cleared.
///
/// <para>
/// Every assertion about an outcome reads the stored rows back, because a 200 is returned whether or not the batch's
/// save really wrote what the report claims.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class BulkProductActionsTests : AuthenticatedIntegrationTestBase
{
    private const string AdminId = "bulk-admin-0001";

    protected override string TestUserId => AdminId;

    private Guid _categoryId;
    private Guid _otherCategoryId;

    [OneTimeSetUp]
    public async Task SeedCategoriesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        _categoryId = await CatalogDataHelper.CreateCategoryAsync(scope.ServiceProvider, "Bulk Source", $"bulk-src-{Guid.NewGuid():N}");
        _otherCategoryId = await CatalogDataHelper.CreateCategoryAsync(scope.ServiceProvider, "Bulk Target", $"bulk-dst-{Guid.NewGuid():N}");
    }

    /// <summary>A per-test marker in every product name, so its list reads have their own cache keys.</summary>
    private static string Marker() => $"bk{Guid.NewGuid():N}"[..12];

    private async Task<Guid> SeedAsync(string marker, bool publish, decimal price = 100m, Guid? categoryId = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, $"Bulk {marker}", CatalogDataHelper.GenerateUniqueSku("BLK"), price, 5,
            categoryId ?? _categoryId, publish);
    }

    private async Task<Product> StoredAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Products.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task<BulkReportResponse> PostOkAsync(string path, object body)
    {
        using var response = await Client.PostAsync(path, JsonContent.Create(body, body.GetType()));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BulkReportResponse>())!;
    }

    private static async Task<List<Guid>> PublicListIdsAsync(HttpClient client, string marker)
        => (await client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
                $"/api/v1/products?PageSize=100&SearchTerm={marker}"))!
            .Items.Select(p => p.Id).ToList();

    // ---------- publish / unpublish ----------

    [Test]
    public async Task BulkPublish_PublishesEachDraft_ReportsEveryIdInOrder_AndTheCachedPublicListCatchesUp()
    {
        var marker = Marker();
        var draftA = await SeedAsync(marker, publish: false);
        var draftB = await SeedAsync(marker, publish: false);
        var alreadyActive = await SeedAsync(marker, publish: true);
        var deleted = await SeedAsync(marker, publish: false);
        await Client.DeleteAsync($"/api/v1/products/{deleted}");
        var missing = Guid.NewGuid();

        // Prime the anonymous list: only the already-published product is in it.
        using var anonymous = Factory.CreateClient();
        (await PublicListIdsAsync(anonymous, marker)).Should().BeEquivalentTo([alreadyActive]);

        var report = await PostOkAsync("/api/v1/products/bulk/publish",
            new BulkProductIdsRequest { ProductIds = [draftA, missing, alreadyActive, deleted, draftB] });

        report.Items.Select(i => i.ProductId).Should().Equal(draftA, missing, alreadyActive, deleted, draftB);
        report.Items.Select(i => i.ErrorCode).Should().Equal(null, "Product.NotFound", null, "Product.NotFound", null);
        (report.Requested, report.Succeeded, report.Failed).Should().Be((5, 3, 2));

        (await StoredAsync(draftA)).Status.Should().Be(ProductStatus.Active);
        (await StoredAsync(draftB)).Status.Should().Be(ProductStatus.Active);
        (await StoredAsync(deleted)).IsDeleted.Should().BeTrue("a deleted product is not found, and not resurrected");

        (await PublicListIdsAsync(anonymous, marker)).Should().BeEquivalentTo(
            [draftA, draftB, alreadyActive], "the list family was bumped, so the page cached above is no longer served");
    }

    [Test]
    public async Task BulkUnpublish_ReturnsEachToDraft_AndThePublicDetailCachedBeforeIsEvicted()
    {
        var marker = Marker();
        var first = await SeedAsync(marker, publish: true);
        var second = await SeedAsync(marker, publish: true);

        using var anonymous = Factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/products/{first}")).StatusCode.Should().Be(HttpStatusCode.OK, "cached now");

        var report = await PostOkAsync("/api/v1/products/bulk/unpublish", new BulkProductIdsRequest { ProductIds = [first, second] });

        report.Succeeded.Should().Be(2);
        (await StoredAsync(first)).Status.Should().Be(ProductStatus.Draft);
        (await StoredAsync(second)).Status.Should().Be(ProductStatus.Draft);
        (await anonymous.GetAsync($"/api/v1/products/{first}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the public detail variant was evicted rather than served from the cache");
    }

    // ---------- delete ----------

    [Test]
    public async Task BulkDelete_SoftDeletesEach_AndEachCanStillBeRestored()
    {
        var marker = Marker();
        var first = await SeedAsync(marker, publish: true);
        var second = await SeedAsync(marker, publish: true);

        using var anonymous = Factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/products/{second}")).StatusCode.Should().Be(HttpStatusCode.OK, "cached now");

        var report = await PostOkAsync("/api/v1/products/bulk/delete", new BulkProductIdsRequest { ProductIds = [first, second] });

        report.Succeeded.Should().Be(2);
        (await StoredAsync(first)).IsDeleted.Should().BeTrue();
        (await StoredAsync(second)).IsDeleted.Should().BeTrue();
        (await anonymous.GetAsync($"/api/v1/products/{second}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A soft delete, not a hard one: the single restore still finds it.
        (await Client.PostAsync($"/api/v1/products/{first}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------- category ----------

    [Test]
    public async Task BulkCategory_MovesEach()
    {
        var marker = Marker();
        var first = await SeedAsync(marker, publish: true);
        var second = await SeedAsync(marker, publish: false);

        var report = await PostOkAsync("/api/v1/products/bulk/category",
            new BulkChangeCategoryRequest { ProductIds = [first, second], CategoryId = _otherCategoryId });

        report.Succeeded.Should().Be(2);
        (await StoredAsync(first)).CategoryId.Should().Be(_otherCategoryId);
        (await StoredAsync(second)).CategoryId.Should().Be(_otherCategoryId);
    }

    [Test]
    public async Task BulkCategory_ToAMissingCategory_Is400_AndMovesNothing()
    {
        var product = await SeedAsync(Marker(), publish: true);

        using var response = await Client.PostAsJsonAsync("/api/v1/products/bulk/category",
            new BulkChangeCategoryRequest { ProductIds = [product], CategoryId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Category.NotFound");
        (await StoredAsync(product)).CategoryId.Should().Be(_categoryId);
    }

    // ---------- price ----------

    [Test]
    public async Task ARefusedProduct_IsLeftExactlyAsItWas_WhileTheRestOfTheBatchCommits()
    {
        var marker = Marker();
        var discounted = await SeedAsync(marker, publish: true, price: 100m);
        (await Client.PutAsJsonAsync($"/api/v1/products/{discounted}/discount", new SetProductDiscountRequest { DiscountPrice = 80m }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var plain = await SeedAsync(marker, publish: false, price: 100m);

        // The admin detail variant, cached before the batch.
        (await Client.GetFromJsonAsync<ProductDetailsResponse>($"/api/v1/products/{plain}"))!.Price.Should().Be(100m);

        var report = await PostOkAsync("/api/v1/products/bulk/price", new BulkPriceRequest
        {
            Items =
            [
                new BulkPriceItem { ProductId = discounted, Price = 50m },
                new BulkPriceItem { ProductId = plain, Price = 42.5m }
            ]
        });

        report.Items[0].ErrorCode.Should().Be(BulkProductProcessor.DomainErrorCode);
        report.Items[0].Error.Should().Contain("discount", "the domain's own reason, as a single PUT would give");
        report.Items[1].Succeeded.Should().BeTrue();

        var refused = await StoredAsync(discounted);
        refused.Price.Should().Be(100m);
        refused.DiscountPrice.Should().Be(80m, "a refused price never drops the discount");
        (await StoredAsync(plain)).Price.Should().Be(42.5m);

        (await Client.GetFromJsonAsync<ProductDetailsResponse>($"/api/v1/products/{plain}"))!.Price.Should().Be(42.5m,
            "the admin detail variant was evicted too, not just the public one");
    }

    // ---------- whole-request refusals ----------

    [Test]
    public async Task AnOverCapRequest_Is400_AndChangesNothing()
    {
        var real = await SeedAsync(Marker(), publish: false);
        var ids = new List<Guid> { real };
        ids.AddRange(Enumerable.Range(0, BulkProductLimits.MaxItemsPerRequest).Select(_ => Guid.NewGuid()));

        using var response = await Client.PostAsJsonAsync("/api/v1/products/bulk/publish", new BulkProductIdsRequest { ProductIds = ids });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(BulkProductLimits.MaxItemsPerRequest.ToString());
        (await StoredAsync(real)).Status.Should().Be(ProductStatus.Draft, "refused whole, never truncated to the first thousand");
    }

    [Test]
    public async Task ARepeatedId_Is400_AndChangesNothing()
    {
        var product = await SeedAsync(Marker(), publish: true);

        using var response = await Client.PostAsJsonAsync("/api/v1/products/bulk/delete",
            new BulkProductIdsRequest { ProductIds = [product, product] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Validation.Failed");
        (await StoredAsync(product)).IsDeleted.Should().BeFalse();
    }

    // ---------- audit ----------

    [Test]
    public async Task ABulkDelete_LeavesOneAuditRowPerProduct_EvenPastThe25thId()
    {
        // 30 products: past SafeRequestRenderer's 25-item cut, which is where one row with the ids in its payload would
        // have stopped naming them.
        var marker = Marker();
        var ids = new List<Guid>();
        for (var i = 0; i < 30; i++)
            ids.Add(await SeedAsync(marker, publish: true));
        var missing = Guid.NewGuid();

        await PostOkAsync("/api/v1/products/bulk/delete", new BulkProductIdsRequest { ProductIds = [.. ids, missing] });

        using var scope = Factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.Action == "BulkDeleteProducts" && e.ActorUserId == AdminId)
            .ToListAsync();

        rows.Where(r => ids.Select(id => id.ToString()).Contains(r.EntityId)).Should().HaveCount(30)
            .And.OnlyContain(r => r.Outcome == AuditOutcome.Succeeded);
        rows.Should().ContainSingle(r => r.EntityId == missing.ToString())
            .Which.ErrorCode.Should().Be("Product.NotFound");

        // And the 30th is found the way an operator would look: by the product.
        var page = await Client.GetFromJsonAsync<AuditLogPageDto>($"/api/v1/admin/audit?entityType=Product&entityId={ids[29]}");
        page!.Items.Select(r => r.Action).Should().Contain("BulkDeleteProducts");
    }
}
