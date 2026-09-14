using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// H4, end to end on real PostgreSQL: <c>GET /api/v1/products/newest</c> pages by a
/// <c>(CreatedAt, Id)</c> cursor.
///
/// <para>
/// Every test works inside a category of its own, because the fixture shares one database and the
/// newest-first order would otherwise interleave other tests' rows. The tie test forces identical
/// timestamps with <c>ExecuteUpdate</c> — the only way to get them deterministically, and the case
/// the old <c>CreatedAt &lt; cursor</c> predicate lost rows on.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class NewestProductsPaginationTests : AuthenticatedIntegrationTestBase
{
    private const string NewestEndpoint = "/api/v1/products/newest";

    private async Task<(Guid CategoryId, List<Guid> ProductIds)> SeedCategoryAsync(int count, bool publish = true)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Newest Category", $"newest-{Guid.NewGuid():N}");

        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            ids.Add(await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, $"Newest {i}", CatalogDataHelper.GenerateUniqueSku("NEW"),
                10m + i, 5, categoryId, publish));
        }

        return (categoryId, ids);
    }

    private async Task SetCreatedAtAsync(Guid categoryId, DateTime createdAt)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Products
            .Where(p => p.CategoryId == categoryId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, createdAt));
    }

    private static string PageUrl(Guid categoryId, int pageSize, string? cursor = null)
        => $"{NewestEndpoint}?CategoryId={categoryId}&PageSize={pageSize}"
           + (cursor is null ? "" : $"&Cursor={Uri.EscapeDataString(cursor)}");

    private static async Task<CursorPagedResponse<ProductResponse>> ReadPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CursorPagedResponse<ProductResponse>>())!;
    }

    [Test]
    public async Task WalkingEveryPage_ReturnsEachProductExactlyOnce_WhenTimestampsTie()
    {
        var (categoryId, created) = await SeedCategoryAsync(5);
        await SetCreatedAtAsync(categoryId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await ReadPageAsync(Client, PageUrl(categoryId, 2, cursor));
            seen.AddRange(page.Items.Select(p => p.Id));
            cursor = page.NextCursor;
            page.HasNextPage.Should().Be(cursor is not null);
            pages++;
            pages.Should().BeLessThan(10, "a cursor that never advances would loop forever");
        }
        while (cursor is not null);

        // The old predicate, CreatedAt < cursor, would have ended after the first page here: every
        // remaining row shares the boundary timestamp, so none is strictly less than it.
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(created);
        pages.Should().Be(3);
    }

    [Test]
    public async Task Items_AreNewestFirst()
    {
        var (categoryId, created) = await SeedCategoryAsync(3);
        var baseTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            for (var i = 0; i < created.Count; i++)
            {
                var id = created[i];
                var at = baseTime.AddMinutes(i);
                await db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, at));
            }
        }

        var page = await ReadPageAsync(Client, PageUrl(categoryId, 10));

        page.Items.Select(p => p.Id).Should().Equal(Enumerable.Reverse(created));
    }

    [Test]
    public async Task ARemainderThatExactlyFillsThePage_ReportsNoNextPage()
    {
        // The old offset-shaped response had hasNextPage = PageNumber < TotalPages, true here.
        var (categoryId, _) = await SeedCategoryAsync(2);

        var page = await ReadPageAsync(Client, PageUrl(categoryId, 2));

        page.Items.Should().HaveCount(2);
        page.NextCursor.Should().BeNull();
        page.HasNextPage.Should().BeFalse();
    }

    [Test]
    public async Task AnUnreadableCursor_Is400_NotTheFirstPage()
    {
        var (categoryId, _) = await SeedCategoryAsync(1);

        var response = await Client.GetAsync(PageUrl(categoryId, 2, "not-a-cursor"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Validation.Failed");
    }

    [Test]
    public async Task TheOffsetEndpoint_RejectsACursor_InsteadOfIgnoringIt()
    {
        var response = await Client.GetAsync("/api/v1/products?Cursor=anything");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Validation.Failed");
    }

    [Test]
    public async Task AnAnonymousCaller_CannotAskForUnpublishedProducts()
    {
        // The endpoint overwrites IncludeUnpublished from the caller's role after binding. Without
        // that overwrite this query string would hand an anonymous caller the drafts.
        var (categoryId, drafts) = await SeedCategoryAsync(2, publish: false);
        var url = PageUrl(categoryId, 10) + "&IncludeUnpublished=true";

        using var anonymous = Factory.CreateClient();
        var publicPage = await ReadPageAsync(anonymous, url);
        publicPage.Items.Should().BeEmpty();

        // And the admin path is genuinely wired, so the empty page above is the filter, not a
        // query that returns nothing for anyone.
        var adminPage = await ReadPageAsync(Client, url);
        adminPage.Items.Select(p => p.Id).Should().BeEquivalentTo(drafts);
    }
}
