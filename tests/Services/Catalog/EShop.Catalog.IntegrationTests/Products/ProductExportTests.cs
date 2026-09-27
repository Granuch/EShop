using System.Net;
using System.Net.Http.Json;
using System.Text;
using EShop.Catalog.Application.Products.Queries.ExportProducts;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Admin panel S16, endpoint #48. <c>GET /api/v1/products/export</c>: the admin list's rows under the admin list's
/// filters, as a CSV file, with the CSV rules Payment's export established.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductExportTests : AuthenticatedIntegrationTestBase
{
    private static readonly string Header =
        "Id,Sku,Name,Description,CategoryId,Status,Price,DiscountPrice,StockQuantity,MainImageUrl,CreatedAt";

    private Guid _categoryId;
    private Guid _otherCategoryId;

    [OneTimeSetUp]
    public async Task SeedCategoriesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        _categoryId = await CatalogDataHelper.CreateCategoryAsync(scope.ServiceProvider, "Export Source", $"exp-{Guid.NewGuid():N}");
        _otherCategoryId = await CatalogDataHelper.CreateCategoryAsync(scope.ServiceProvider, "Export Other", $"exp-{Guid.NewGuid():N}");
    }

    private static string Marker() => $"ex{Guid.NewGuid():N}"[..12];

    private async Task<Guid> SeedAsync(string marker, bool publish = true, decimal price = 10m, int stock = 5, Guid? categoryId = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, $"Export {marker}", CatalogDataHelper.GenerateUniqueSku("EXP"), price, stock,
            categoryId ?? _categoryId, publish);
    }

    /// <summary>The data lines of an export, without the BOM and header.</summary>
    private async Task<List<string>> ExportLinesAsync(string query)
    {
        using var response = await Client.GetAsync($"/api/v1/products/export?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        var lines = text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        lines[0].Should().Be(Header);
        return lines.Skip(1).ToList();
    }

    private static List<Guid> IdsOf(IEnumerable<string> lines) => lines.Select(l => Guid.Parse(l[1..37])).ToList();

    [Test]
    public async Task TheExport_IsADownloadableUtf8CsvFile()
    {
        var marker = Marker();
        await SeedAsync(marker);

        using var response = await Client.GetAsync($"/api/v1/products/export?SearchTerm={marker}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment",
            "Results.Text would render in the browser instead of saving");
        response.Content.Headers.ContentDisposition.FileName.Should().StartWith("products-").And.EndWith(".csv");
        (await response.Content.ReadAsByteArrayAsync()).Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
    }

    [Test]
    public async Task TheExport_HoldsDrafts_ButNotDeletedProducts()
    {
        var marker = Marker();
        var active = await SeedAsync(marker, publish: true);
        var draft = await SeedAsync(marker, publish: false);
        var deleted = await SeedAsync(marker);
        (await Client.DeleteAsync($"/api/v1/products/{deleted}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        IdsOf(await ExportLinesAsync($"SearchTerm={marker}")).Should().BeEquivalentTo([active, draft]);
    }

    [Test]
    public async Task EachFilter_NarrowsTheExport_AsItNarrowsTheList()
    {
        var marker = Marker();
        var draft = await SeedAsync(marker, publish: false, stock: 1);
        var elsewhere = await SeedAsync(marker, categoryId: _otherCategoryId, stock: 50);
        var cheap = await SeedAsync(marker, price: 2m, stock: 50);

        IdsOf(await ExportLinesAsync($"SearchTerm={marker}&Status=Draft")).Should().Equal(draft);
        IdsOf(await ExportLinesAsync($"SearchTerm={marker}&CategoryId={_otherCategoryId}")).Should().Equal(elsewhere);
        IdsOf(await ExportLinesAsync($"SearchTerm={marker}&StockBelow=2")).Should().Equal(draft);
        IdsOf(await ExportLinesAsync($"SearchTerm={marker}&MaxPrice=5")).Should().Equal(cheap);
    }

    [Test]
    public async Task TheExport_IsInTheListsOrder()
    {
        var marker = Marker();
        var mid = await SeedAsync(marker, price: 20m);
        var high = await SeedAsync(marker, price: 30m);
        var low = await SeedAsync(marker, price: 10m);

        IdsOf(await ExportLinesAsync($"SearchTerm={marker}&SortBy=Price&IsDescending=true")).Should().Equal(high, mid, low);
    }

    [Test]
    public async Task AFieldASpreadsheetWouldRun_IsMadeInert_AndACommaOrQuoteDoesNotShiftColumns()
    {
        var marker = Marker();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var product = Domain.Entities.Product.Create(
                $"=HYPERLINK(\"http://evil\") {marker}", CatalogDataHelper.GenerateUniqueSku("EXP"), 10m, 1, _categoryId,
                "Soft, \"warm\" wool");
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var line = (await ExportLinesAsync($"SearchTerm={marker}")).Single();

        line.Should().Contain($",\"'=HYPERLINK(\"\"http://evil\"\") {marker}\",");
        line.Should().Contain(",\"Soft, \"\"warm\"\" wool\",");
    }

    [Test]
    public async Task ADateWithNoZone_Filters_RatherThanAnswering500()
    {
        // ?CreatedFrom=2026-01-01 binds as DateTimeKind.Unspecified, which Npgsql refuses against timestamptz. The shared
        // ProductFilterQuery.ToFilter coerces it — for the export and, since this stage, for the list too.
        var marker = Marker();
        var product = await SeedAsync(marker);

        IdsOf(await ExportLinesAsync($"SearchTerm={marker}&CreatedFrom=2026-01-01")).Should().Equal(product);

        using var list = await Client.GetAsync($"/api/v1/products?SearchTerm={marker}&CreatedFrom=2026-01-01");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task TheExport_ValidatesLikeTheList()
    {
        using var response = await Client.GetAsync("/api/v1/products/export?MinPrice=-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// The export's cap, in its own fixture: it seeds more than <see cref="ExportProductsQuery.MaxRows"/> products, which no
/// other fixture should have to share a database with.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductExportCapTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task AnExportLargerThanTheCap_IsRefused_RatherThanTruncated_ButTheCapIsOnTheFilteredCount()
    {
        var marker = $"cap{Guid.NewGuid():N}"[..12];
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

            // Raw SQL: ten thousand tracked inserts would dominate the suite's run time for no extra coverage.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Products" ("Id", "Name", "Sku", "Price", "StockQuantity", "Status", "CategoryId", "IsDeleted", "CreatedAt", "Version")
                SELECT gen_random_uuid(), 'Cap ' || {0} || ' ' || g, {0} || '-' || g, 10, 1, 1, {1}, false, now(), 0
                FROM generate_series(1, {2}) AS g
                """,
                marker, categoryId, ExportProductsQuery.MaxRows + 1);

            await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Small export", CatalogDataHelper.GenerateUniqueSku("SMALL"), 10m, 1, categoryId);
        }

        using var tooMany = await Client.GetAsync($"/api/v1/products/export?SearchTerm={marker}");
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = (await tooMany.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!;
        problem.ErrorCode.Should().Be("Products.ExportTooLarge");
        problem.Detail.Should().Contain((ExportProductsQuery.MaxRows + 1).ToString()).And.Contain(ExportProductsQuery.MaxRows.ToString());

        using var narrowed = await Client.GetAsync("/api/v1/products/export?SearchTerm=Small export");
        narrowed.StatusCode.Should().Be(HttpStatusCode.OK, "the table is over the cap, the filtered set is not");
    }
}
