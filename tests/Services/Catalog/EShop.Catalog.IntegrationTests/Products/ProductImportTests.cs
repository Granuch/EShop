using System.Net;
using System.Net.Http.Json;
using System.Text;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Application.Products.Commands.ImportProducts;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Fixtures;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Admin panel S16, endpoint #47. <c>POST /api/v1/products/import</c> on real Postgres: good rows become drafts, every
/// other row is reported by index and creates nothing.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductImportTests : AuthenticatedIntegrationTestBase
{
    private const string AdminId = "import-admin-0001";

    protected override string TestUserId => AdminId;

    private Guid _categoryId;

    [OneTimeSetUp]
    public async Task SeedCategoryAsync()
    {
        using var scope = Factory.Services.CreateScope();
        _categoryId = await CatalogDataHelper.CreateCategoryAsync(scope.ServiceProvider, "Import Target", $"import-{Guid.NewGuid():N}");
    }

    private static string Marker() => $"im{Guid.NewGuid():N}"[..12];

    private ImportProductRowRequest Row(string sku, string name, decimal price = 10m, Guid? categoryId = null) => new()
    {
        Sku = sku,
        Name = name,
        Description = "Imported description",
        Price = price,
        StockQuantity = 7,
        CategoryId = categoryId ?? _categoryId
    };

    private async Task<List<Product>> StoredWithSkuPrefixAsync(string prefix)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Products.AsNoTracking().Where(p => p.Sku.StartsWith(prefix)).ToListAsync();
    }

    [Test]
    public async Task AnImport_CreatesEveryGoodRow_AsADraft_AndRefusesEveryOtherRowByIndex()
    {
        var marker = Marker();
        var taken = $"{marker}-TAKEN";
        using (var scope = Factory.Services.CreateScope())
        {
            await CatalogDataHelper.CreateProductAsync(scope.ServiceProvider, "Holder", taken, 5m, 1, _categoryId);
        }

        // The admin list, cached before the import, must show the new drafts afterwards.
        (await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>($"/api/v1/products?PageSize=100&SearchTerm={marker}"))!
            .Items.Should().ContainSingle();

        using var response = await Client.PostAsJsonAsync("/api/v1/products/import", new ImportProductsRequest
        {
            Products =
            [
                Row($"{marker}-A", $"Import {marker} A", price: 12.34m),
                Row($"{marker} BAD", $"Import {marker} bad sku"),
                Row(taken, $"Import {marker} taken"),
                Row($"{marker}-DUP", $"Import {marker} dup 1"),
                Row($"{marker}-NOCAT", $"Import {marker} nocat", categoryId: Guid.NewGuid()),
                Row($"{marker}-DUP", $"Import {marker} dup 2"),
                Row($"{marker}-B", $"Import {marker} B")
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = (await response.Content.ReadFromJsonAsync<ImportReportResponse>())!;

        report.Rows.Select(r => r.Index).Should().Equal(0, 1, 2, 3, 4, 5, 6);
        report.Rows.Select(r => r.ErrorCode).Should().Equal(
            null, "Validation.Failed", "Product.SkuConflict", "Product.SkuConflict", "Category.NotFound", "Product.SkuConflict", null);
        (report.Requested, report.Created, report.Failed).Should().Be((7, 2, 5));

        var stored = await StoredWithSkuPrefixAsync(marker);
        stored.Select(p => p.Sku).Should().BeEquivalentTo([taken, $"{marker}-A", $"{marker}-B"],
            "no refused row created anything, the duplicated SKU included");
        var a = stored.Single(p => p.Sku == $"{marker}-A");
        a.Status.Should().Be(ProductStatus.Draft);
        a.Price.Should().Be(12.34m);
        a.StockQuantity.Should().Be(7);
        a.Description.Should().Be("Imported description");
        a.CategoryId.Should().Be(_categoryId);
        report.Rows[0].ProductId.Should().Be(a.Id);

        (await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>($"/api/v1/products?PageSize=100&SearchTerm={marker}"))!
            .Items.Should().HaveCount(3, "the list family was bumped by the import");
    }

    [Test]
    public async Task EachImportRow_IsAudited_ARefusedOneWithoutAnEntity()
    {
        var marker = Marker();

        using var response = await Client.PostAsJsonAsync("/api/v1/products/import", new ImportProductsRequest
        {
            Products = [Row($"{marker}-OK", $"Import {marker} ok"), Row($"{marker}-X", "", price: 1m)]
        });
        var report = (await response.Content.ReadFromJsonAsync<ImportReportResponse>())!;

        using var scope = Factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.Action == "ImportProducts" && e.PayloadJson!.Contains(marker))
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(r => r.EntityId == report.Rows[0].ProductId.ToString())
            .Which.Outcome.Should().Be(AuditOutcome.Succeeded);
        var refused = rows.Single(r => r.EntityId == null);
        refused.Outcome.Should().Be(AuditOutcome.Rejected);
        refused.ErrorCode.Should().Be("Validation.Failed");
        refused.PayloadJson.Should().Contain($"{marker}-X").And.Contain("\"index\":1");
    }

    [Test]
    public async Task AnOverCapImport_Is400_AndCreatesNothing()
    {
        var marker = Marker();
        var rows = Enumerable.Range(0, ImportProductsCommand.MaxRows + 1)
            .Select(i => Row($"{marker}-{i}", $"Import {marker} {i}"))
            .ToList();

        using var response = await Client.PostAsJsonAsync("/api/v1/products/import", new ImportProductsRequest { Products = rows });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredWithSkuPrefixAsync(marker)).Should().BeEmpty("refused whole, never truncated");
    }

    [Test]
    public async Task AnUnknownFieldInARow_RefusesTheRequest_BeforeAnyRowIsCreated()
    {
        // Rows are typed: a column the API does not know is refused as it is everywhere in Catalog, not dropped.
        var marker = Marker();
        var json = $$"""
            {"products":[
              {"name":"Import {{marker}}","sku":"{{marker}}-OK","price":10,"stockQuantity":1,"categoryId":"{{_categoryId}}"},
              {"name":"Import {{marker}}","sku":"{{marker}}-EXTRA","price":10,"stockQuantity":1,"categoryId":"{{_categoryId}}","colour":"red"}
            ]}
            """;

        using var response = await Client.PostAsync("/api/v1/products/import", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("colour");
        (await StoredWithSkuPrefixAsync(marker)).Should().BeEmpty();
    }
}

/// <summary>
/// The import's database backstop. With the SKU pre-check blinded, a row whose SKU a live product holds reaches
/// <c>IX_Products_Sku</c> at the single save — and the whole import must roll back, rather than report rows created that
/// were not.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductImportConflictTests : AuthenticatedIntegrationTestBase
{
    protected override async Task<CatalogApiFactory> CreateFactoryAsync() => await BlindSkuCheckApiFactory.CreateAsync();

    [Test]
    public async Task ASkuTakenBehindThePreChecksBack_Is409_AndNoRowOfTheImportIsCreated()
    {
        var marker = $"ic{Guid.NewGuid():N}"[..12];
        var taken = $"{marker}-TAKEN";
        Guid categoryId;
        using (var scope = Factory.Services.CreateScope())
        {
            categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
            await CatalogDataHelper.CreateProductAsync(scope.ServiceProvider, "Holder", taken, 5m, 1, categoryId);
        }

        using var response = await Client.PostAsJsonAsync("/api/v1/products/import", new ImportProductsRequest
        {
            Products =
            [
                new ImportProductRowRequest { Sku = $"{marker}-NEW", Name = "Fresh", Price = 10m, StockQuantity = 1, CategoryId = categoryId },
                new ImportProductRowRequest { Sku = taken, Name = "Collides", Price = 10m, StockQuantity = 1, CategoryId = categoryId }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Product.SkuConflict");

        using var check = Factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<CatalogDbContext>().Products.AsNoTracking()
                .AnyAsync(p => p.Sku == $"{marker}-NEW"))
            .Should().BeFalse("the good row went down with the batch, so no report ever named a product that is not there");
    }
}

/// <summary>
/// Admin panel S16's named risk: the <c>products:list</c> family is bumped once per bulk request, not once per product.
/// </summary>
[TestFixture]
[Category("Integration")]
public class BulkProductCacheFamilyTests : AuthenticatedIntegrationTestBase
{
    private CountingCacheVersionApiFactory CountingFactory => (CountingCacheVersionApiFactory)Factory;

    protected override async Task<CatalogApiFactory> CreateFactoryAsync() => await CountingCacheVersionApiFactory.CreateAsync();

    private int ListBumps => CountingFactory.Bumps.GetValueOrDefault(ProductCacheFamilies.ProductList);

    [Test]
    public async Task ABulkActionOnFiveProducts_BumpsTheListFamilyOnce()
    {
        var ids = new List<Guid>();
        using (var scope = Factory.Services.CreateScope())
        {
            var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
            for (var i = 0; i < 5; i++)
            {
                ids.Add(await CatalogDataHelper.CreateProductAsync(
                    scope.ServiceProvider, "Family", CatalogDataHelper.GenerateUniqueSku("FAM"), 10m, 1, categoryId, publish: false));
            }
        }

        var before = ListBumps;
        (await Client.PostAsJsonAsync("/api/v1/products/bulk/publish", new BulkProductIdsRequest { ProductIds = ids }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (ListBumps - before).Should().Be(1);
    }

    [Test]
    public async Task AnImportOfThreeRows_BumpsTheListFamilyOnce()
    {
        Guid categoryId;
        using (var scope = Factory.Services.CreateScope())
            categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var before = ListBumps;
        (await Client.PostAsJsonAsync("/api/v1/products/import", new ImportProductsRequest
        {
            Products = Enumerable.Range(0, 3).Select(i => new ImportProductRowRequest
            {
                Sku = CatalogDataHelper.GenerateUniqueSku("FAMI"), Name = $"Family {i}", Price = 10m, StockQuantity = 1, CategoryId = categoryId
            }).ToList()
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (ListBumps - before).Should().Be(1);
    }
}
