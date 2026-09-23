using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Auditing;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Catalog.Application.Administration;
using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Products;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Fixtures;
using EShop.Catalog.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Administration;

/// <summary>
/// Admin panel S19, endpoint #88: <c>POST /api/v1/admin/cache/invalidate</c>.
///
/// <para>
/// A bump is not observable in the cache itself — nothing is deleted — so these tests read it two ways: by counting the
/// provider's calls (<see cref="CountingCacheVersionApiFactory"/>), and end to end, by changing a row <b>behind the
/// application's back</b> and watching an already-cached list stay stale until the lever is pulled. The second is the
/// one that proves the family the endpoint bumps is the family the queries read; a counted bump of a misspelled family
/// would pass the first and fail the second.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CacheInvalidationTests : AuthenticatedIntegrationTestBase
{
    private const string Path = "/api/v1/admin/cache/invalidate";

    private CountingCacheVersionApiFactory Counting => (CountingCacheVersionApiFactory)Factory;

    protected override async Task<CatalogApiFactory> CreateFactoryAsync() => await CountingCacheVersionApiFactory.CreateAsync();

    [TearDown]
    public void StopFailing() => Counting.FailingFamily = null;

    private int BumpsOf(string family) => Counting.Bumps.GetValueOrDefault(family);

    private Task<HttpResponseMessage> InvalidateAsync(string? family = null)
        => Client.PostAsync(family is null ? Path : $"{Path}?family={Uri.EscapeDataString(family)}", content: null);

    private static async Task<string> ErrorCodeOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errorCode").GetString()!;
    }

    // ---------- what is bumped ----------

    [Test]
    public async Task WithNoFamily_EveryCatalogFamilyIsBumpedOnce_AndNamedInTheAnswer()
    {
        var before = CatalogCacheFamilies.All.ToDictionary(f => f, BumpsOf);

        using var response = await InvalidateAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = await response.Content.ReadFromJsonAsync<ReportResponse>();
        report!.Service.Should().Be("catalog");
        report.Families.Should().Equal(CatalogCacheFamilies.All);
        foreach (var family in CatalogCacheFamilies.All)
        {
            (BumpsOf(family) - before[family]).Should().Be(1, family);
        }
    }

    [Test]
    public async Task ANamedFamily_BumpsThatFamilyAlone()
    {
        var products = BumpsOf(ProductCacheFamilies.ProductList);
        var categories = BumpsOf(CategoryCacheFamilies.CategoryList);

        using var response = await InvalidateAsync(ProductCacheFamilies.ProductList);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReportResponse>())!.Families.Should().Equal(ProductCacheFamilies.ProductList);
        (BumpsOf(ProductCacheFamilies.ProductList) - products).Should().Be(1);
        BumpsOf(CategoryCacheFamilies.CategoryList).Should().Be(categories);
    }

    [TestCase("products")]
    [TestCase("Products:List")]
    [TestCase("products:list:v2")]
    public async Task AFamilyCatalogDoesNotHave_Is400_AndNothingIsBumped(string family)
    {
        // "Products:List" is the case that matters: the family is part of the version entry's key, so accepting it would
        // bump cachever:Products:List — an entry no query reads — and answer 200 having invalidated nothing.
        var before = Counting.Bumps.Values.Sum();

        using var response = await InvalidateAsync(family);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("Validation.Failed");
        Counting.Bumps.Values.Sum().Should().Be(before);
    }

    [Test]
    public async Task ACacheThatCannotBeReached_Is503_NamingWhatWasAndWasNotInvalidated_WithoutTheCachesAddress()
    {
        Counting.FailingFamily = CategoryCacheFamilies.CategoryList;

        using var response = await InvalidateAsync();
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, text);
        (await ErrorCodeOf(response)).Should().Be("Cache.Unavailable");
        text.Should().Contain(ProductCacheFamilies.ProductList).And.Contain(CategoryCacheFamilies.CategoryList);
        text.Should().NotContain("redis-secret-host", "the exception text names the cache's address and belongs in the log");
    }

    // ---------- the bump reaches the queries ----------

    [Test]
    public async Task AProductListCached_BeforeAnOutOfBandChange_IsStaleUntilTheListFamilyIsInvalidated()
    {
        Guid categoryId, productId;
        using (var scope = Factory.Services.CreateScope())
        {
            categoryId = await CatalogDataHelper.CreateCategoryAsync(scope.ServiceProvider, "Cache Lever", $"cache-lever-{Guid.NewGuid():N}");
            productId = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Before The Change", CatalogDataHelper.GenerateUniqueSku("LEVER"), 10m, 1, categoryId);
        }

        var list = $"/api/v1/products?CategoryId={categoryId}&PageSize=10";
        (await Client.GetStringAsync(list)).Should().Contain("Before The Change");

        await RenameBehindTheApplicationsBackAsync(db => db.Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "After The Change")));

        (await Client.GetStringAsync(list)).Should().Contain("Before The Change",
            "the list is cached and nothing the application did invalidated it — otherwise this test proves nothing");

        (await InvalidateAsync(ProductCacheFamilies.ProductList)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Client.GetStringAsync(list)).Should().Contain("After The Change").And.NotContain("Before The Change");
    }

    [Test]
    public async Task TheCategoryTree_CachedBeforeAnOutOfBandChange_IsStaleUntilTheCategoryFamilyIsInvalidated()
    {
        Guid categoryId;
        var before = $"Tree Before {Guid.NewGuid():N}";
        var after = $"Tree After {Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            categoryId = await CatalogDataHelper.CreateCategoryAsync(scope.ServiceProvider, before, $"tree-{Guid.NewGuid():N}");
        }

        // Invalidated first so the read below repopulates the tree with this category in it.
        (await InvalidateAsync(CategoryCacheFamilies.CategoryList)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetStringAsync("/api/v1/categories")).Should().Contain(before);

        await RenameBehindTheApplicationsBackAsync(db => db.Categories.Where(c => c.Id == categoryId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, after)));

        (await Client.GetStringAsync("/api/v1/categories")).Should().Contain(before, "the tree is cached");

        (await InvalidateAsync(CategoryCacheFamilies.CategoryList)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Client.GetStringAsync("/api/v1/categories")).Should().Contain(after).And.NotContain(before);
    }

    private async Task RenameBehindTheApplicationsBackAsync(Func<CatalogDbContext, Task<int>> update)
    {
        using var scope = Factory.Services.CreateScope();
        (await update(scope.ServiceProvider.GetRequiredService<CatalogDbContext>())).Should().Be(1);
    }

    // ---------- audit ----------

    [Test]
    public async Task EachFamilyBumped_IsAuditedAsItsOwnRow()
    {
        var marker = $"cache-{Guid.NewGuid():N}";
        using (var request = new HttpRequestMessage(HttpMethod.Post, Path))
        {
            request.Headers.Add("X-Correlation-ID", marker);
            (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var page = await Client.GetFromJsonAsync<AuditLogPageDto>("/api/v1/admin/audit?entityType=CacheFamily&pageSize=100");
        var rows = page!.Items.Where(r => r.CorrelationId == marker).ToList();

        rows.Select(r => r.EntityId).Should().BeEquivalentTo(CatalogCacheFamilies.All);
        rows.Should().OnlyContain(r => r.Action == "InvalidateCacheFamilies" && r.Outcome == "Succeeded");
    }

    // ---------- authorization ----------

    [Test]
    public async Task TheSystemManagePermission_IsEnough_WithoutTheAdminRole()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Path}?family={ProductCacheFamilies.ProductList}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            CreateToken(role: null, new Claim(EShopPermissions.ClaimType, EShopPermissions.SystemManage)));

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ACatalogPermission_IsNot()
    {
        var before = Counting.Bumps.Values.Sum();
        using var request = new HttpRequestMessage(HttpMethod.Post, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            CreateToken(role: null, new Claim(EShopPermissions.ClaimType, EShopPermissions.CatalogWrite)));

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Counting.Bumps.Values.Sum().Should().Be(before);
    }

    [Test]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        using var anonymous = Factory.CreateClient();
        (await anonymous.PostAsync(Path, content: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record ReportResponse(string Service, List<string> Families);
}
