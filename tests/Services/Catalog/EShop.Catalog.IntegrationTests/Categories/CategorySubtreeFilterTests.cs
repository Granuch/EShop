using System.Net;
using System.Net.Http.Json;
using System.Text;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Categories;

/// <summary>
/// A category filter means the category <b>and its whole subtree</b>, on every read that takes one.
///
/// <para>
/// It used to be an exact match on <c>Product.CategoryId</c>, so a parent category such as
/// "Electronics" listed nothing while its children held every product. The tree has no depth limit,
/// so each test seeds three levels — a filter that only reached direct children would pass a
/// two-level fixture and silently drop grandchildren.
/// </para>
///
/// <para>
/// Every test seeds its own tree under unique slugs: the host is fixture-scoped, so tests share a
/// database <i>and</i> Redis, and two tests requesting the same URL would share a cached page.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class CategorySubtreeFilterTests : AuthenticatedIntegrationTestBase
{
    private const string CategoriesEndpoint = "/api/v1/categories";
    private const string ProductsEndpoint = "/api/v1/products";

    /// <summary>
    /// Root → {Child → Grandchild, Sibling}, one product in each, plus a childless root and an
    /// unrelated root that must never leak in.
    /// </summary>
    private sealed record Tree(
        Guid Root, Guid Child, Guid Grandchild, Guid Sibling, Guid ChildlessRoot, Guid OtherRoot,
        Guid InRoot, Guid InChild, Guid InGrandchild, Guid InSibling, Guid InChildlessRoot, Guid InOtherRoot);

    private async Task<Tree> SeedTreeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        async Task<Guid> Category(string name, Guid? parent = null)
            => await CatalogDataHelper.CreateCategoryAsync(services, name, $"sub-{Guid.NewGuid():N}", parent);

        async Task<Guid> Product(Guid categoryId)
            => await CatalogDataHelper.CreateProductAsync(
                services, "Subtree product", CatalogDataHelper.GenerateUniqueSku("SUB"), 10m, 5, categoryId);

        var root = await Category("Electronics");
        var child = await Category("Laptops", root);
        var grandchild = await Category("Gaming laptops", child);
        var sibling = await Category("Phones", root);
        var childlessRoot = await Category("Books");
        var otherRoot = await Category("Garden");

        return new Tree(
            root, child, grandchild, sibling, childlessRoot, otherRoot,
            await Product(root), await Product(child), await Product(grandchild), await Product(sibling),
            await Product(childlessRoot), await Product(otherRoot));
    }

    private async Task<PagedResponse<ProductResponse>> ListAsync(Guid categoryId, HttpClient? client = null)
    {
        var response = await (client ?? Client).GetAsync(
            $"{ProductsEndpoint}?CategoryId={categoryId}&PageNumber=1&PageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>())!;
    }

    private async Task<PagedResponse<ProductResponse>> CategoryPageAsync(Guid categoryId)
    {
        var response = await Client.GetAsync($"{CategoriesEndpoint}/{categoryId}/products?PageNumber=1&PageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>())!;
    }

    #region GET /products

    [Test]
    public async Task AParentCategory_ListsEveryLevelBelowIt_WithAnAccurateTotal()
    {
        var tree = await SeedTreeAsync();

        var page = await ListAsync(tree.Root);

        page.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [tree.InRoot, tree.InChild, tree.InGrandchild, tree.InSibling],
            "the grandchild's product is the one a direct-children-only filter would drop");
        page.TotalCount.Should().Be(4, "the count and the page must use the same resolved subtree");
    }

    [Test]
    public async Task AMiddleCategory_ListsItselfAndItsDescendants_NotItsParentOrSiblings()
    {
        var tree = await SeedTreeAsync();

        var page = await ListAsync(tree.Child);

        page.Items.Select(p => p.Id).Should().BeEquivalentTo([tree.InChild, tree.InGrandchild]);
    }

    [Test]
    public async Task ALeafCategory_ListsOnlyItsOwnProducts()
    {
        var tree = await SeedTreeAsync();

        var page = await ListAsync(tree.Grandchild);

        page.Items.Select(p => p.Id).Should().Equal(tree.InGrandchild);
    }

    [Test]
    public async Task ARootWithNoChildren_ListsOnlyItsOwnProducts()
    {
        var tree = await SeedTreeAsync();

        var page = await ListAsync(tree.ChildlessRoot);

        page.Items.Select(p => p.Id).Should().Equal(tree.InChildlessRoot);
    }

    [Test]
    public async Task AnUnknownCategory_IsStillAnEmptyPage()
    {
        var page = await ListAsync(Guid.NewGuid());

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Test]
    public async Task TheSubtree_KeepsThePublishedOnlyRule_ForAnonymousCallers()
    {
        var tree = await SeedTreeAsync();
        Guid draft;
        using (var scope = Factory.Services.CreateScope())
        {
            draft = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Subtree draft", CatalogDataHelper.GenerateUniqueSku("SUB"), 10m, 5,
                tree.Grandchild, publish: false);
        }

        using var anonymous = Factory.CreateClient();
        var publicPage = await ListAsync(tree.Root, anonymous);
        publicPage.Items.Select(p => p.Id).Should().NotContain(draft);
        publicPage.TotalCount.Should().Be(4);

        var adminPage = await ListAsync(tree.Root);
        adminPage.Items.Select(p => p.Id).Should().Contain(draft);
        adminPage.TotalCount.Should().Be(5);
    }

    #endregion

    #region The other reads that take a category

    [Test]
    public async Task TheCategoryProductsEndpoint_ListsTheWholeSubtree()
    {
        var tree = await SeedTreeAsync();

        var page = await CategoryPageAsync(tree.Root);

        page.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [tree.InRoot, tree.InChild, tree.InGrandchild, tree.InSibling]);
        page.TotalCount.Should().Be(4);
    }

    [Test]
    public async Task TheNewestList_ListsTheWholeSubtree()
    {
        var tree = await SeedTreeAsync();

        var response = await Client.GetAsync($"{ProductsEndpoint}/newest?CategoryId={tree.Root}&PageSize=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = (await response.Content.ReadFromJsonAsync<CursorPagedResponse<ProductResponse>>())!;

        page.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [tree.InRoot, tree.InChild, tree.InGrandchild, tree.InSibling]);
    }

    [Test]
    public async Task TheExport_ListsTheWholeSubtree()
    {
        var tree = await SeedTreeAsync();

        using var response = await Client.GetAsync($"{ProductsEndpoint}/export?CategoryId={tree.Root}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
        var dataLines = text.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1);

        // Parse the whole line for GUIDs rather than a fixed column slice: the row's CategoryId is a
        // GUID too, so collect only those that are product ids.
        var productIds = new[] { tree.InRoot, tree.InChild, tree.InGrandchild, tree.InSibling };
        var exported = dataLines
            .SelectMany(line => line.Split(','))
            .Select(cell => Guid.TryParse(cell.Trim('"'), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        exported.Should().Contain(productIds);
        exported.Should().NotContain([tree.InChildlessRoot, tree.InOtherRoot]);
        dataLines.Should().HaveCount(4);
    }

    [Test]
    public async Task TheLowStockDashboard_ListsTheWholeSubtree()
    {
        var tree = await SeedTreeAsync();

        var page = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"/api/v1/admin/catalog/low-stock?threshold=10&CategoryId={tree.Root}&PageSize=100");

        page!.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [tree.InRoot, tree.InChild, tree.InGrandchild, tree.InSibling]);
    }

    [Test]
    public async Task TheRecycleBin_FindsADeletedProduct_InADeletedSubcategory()
    {
        // The case that decides how the subtree is walked. A deleted category can hold deleted
        // products (a category delete only counts live ones), and the recycle bin lifts every query
        // filter. A walk that skipped inactive categories would lose this product from its
        // grandparent's bin even though the exact match on its own category still finds it.
        var tree = await SeedTreeAsync();
        (await Client.DeleteAsync($"{ProductsEndpoint}/{tree.InGrandchild}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{tree.Grandchild}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var bin = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{ProductsEndpoint}/deleted?CategoryId={tree.Root}&PageNumber=1&PageSize=100");

        bin!.Items.Select(p => p.Id).Should().Equal(tree.InGrandchild);
    }

    [Test]
    public async Task Stats_CountTheWholeSubtree_WhileChildCountStaysOneLevel()
    {
        var tree = await SeedTreeAsync();

        var stats = await Client.GetFromJsonAsync<CategoryStatsResponse>($"{CategoriesEndpoint}/{tree.Root}/stats");

        stats!.ProductCount.Should().Be(4, "the number must match what clicking into the category lists");
        stats.PublishedProductCount.Should().Be(4);
        stats.TotalStock.Should().Be(20);
        stats.ChildCategoryCount.Should().Be(2, "Laptops and Phones — the grandchild is not a direct child");
    }

    #endregion

    #region Cache

    [Test]
    public async Task MovingACategoryIntoTheTree_ShowsItsProducts_OnTheRootsAlreadyCachedPages()
    {
        // The keys did not change with this feature — only what a category id means — so the one new
        // way for a cached page to go stale is the tree changing shape under it. MoveCategory bumps
        // products:list for exactly this; the test goes through HTTP because only that exercises
        // CachingBehavior.
        var tree = await SeedTreeAsync();
        Guid outsider, outsiderProduct;
        using (var scope = Factory.Services.CreateScope())
        {
            outsider = await CatalogDataHelper.CreateCategoryAsync(
                scope.ServiceProvider, "Outsider", $"sub-{Guid.NewGuid():N}");
            outsiderProduct = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Outsider product", CatalogDataHelper.GenerateUniqueSku("SUB"), 10m, 5, outsider);
        }

        (await ListAsync(tree.Root)).TotalCount.Should().Be(4);
        (await CategoryPageAsync(tree.Root)).TotalCount.Should().Be(4);

        var move = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{outsider}/parent",
            new MoveCategoryRequest { NewParentCategoryId = tree.Grandchild });
        move.StatusCode.Should().Be(HttpStatusCode.NoContent, await move.Content.ReadAsStringAsync());

        (await ListAsync(tree.Root)).Items.Select(p => p.Id).Should().Contain(outsiderProduct);
        (await CategoryPageAsync(tree.Root)).Items.Select(p => p.Id).Should().Contain(outsiderProduct);
    }

    #endregion
}
