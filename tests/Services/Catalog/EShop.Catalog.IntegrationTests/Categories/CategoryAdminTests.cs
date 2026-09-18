using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Categories;

/// <summary>
/// Admin panel S5 — category re-parenting, sibling reorder, restore, the admin view of the tree,
/// and per-category stats.
/// </summary>
/// <remarks>
/// The cache tests here are the point of the stage. A4: <c>GET /categories</c> used to be one fixed
/// key evicted by exact string, and adding <c>?includeInactive</c> naively is a cache-level data
/// leak — an admin's request would populate the shared entry and every anonymous caller would read
/// deactivated categories out of it for the full ten-minute TTL.
/// </remarks>
[TestFixture]
[Category("Integration")]
public class CategoryAdminTests : AuthenticatedIntegrationTestBase
{
    private const string CategoriesEndpoint = "/api/v1/categories";

    private async Task<Guid> SeedCategoryAsync(string name, Guid? parentId = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, name, parentCategoryId: parentId);
    }

    private async Task<Domain.Entities.Category?> StoredAsync(Guid categoryId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Categories.AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.Id == categoryId);
    }

    #region MoveTo

    [Test]
    public async Task Move_ReParentsTheCategory()
    {
        var newParent = await SeedCategoryAsync($"S5 new parent {Guid.NewGuid():N}");
        var child = await SeedCategoryAsync($"S5 child {Guid.NewGuid():N}");

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{child}/parent",
            new MoveCategoryRequest { NewParentCategoryId = newParent });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await StoredAsync(child))!.ParentCategoryId.Should().Be(newParent);
    }

    [Test]
    public async Task Move_ToNull_PromotesToRoot()
    {
        var parent = await SeedCategoryAsync($"S5 parent {Guid.NewGuid():N}");
        var child = await SeedCategoryAsync($"S5 child {Guid.NewGuid():N}", parent);

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{child}/parent",
            new MoveCategoryRequest { NewParentCategoryId = null });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredAsync(child))!.ParentCategoryId.Should().BeNull();
    }

    [Test]
    public async Task Move_UnderItsOwnDescendant_Is400_AndChangesNothing()
    {
        // The cycle that only a persisted ancestor chain can detect. Grandchild is two levels below
        // root, so an in-memory navigation walk that stopped at one Include would not see the link.
        var root = await SeedCategoryAsync($"S5 cycle root {Guid.NewGuid():N}");
        var child = await SeedCategoryAsync($"S5 cycle child {Guid.NewGuid():N}", root);
        var grandchild = await SeedCategoryAsync($"S5 cycle grandchild {Guid.NewGuid():N}", child);

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{root}/parent",
            new MoveCategoryRequest { NewParentCategoryId = grandchild });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredAsync(root))!.ParentCategoryId.Should().BeNull("a refused move must change nothing");
    }

    [Test]
    public async Task Move_UnderADeepDescendant_Is400_WhereNavigationAloneCannotSeeIt()
    {
        // The test that actually discriminates. A shallower cycle is caught even by a
        // navigation-walking check, because EF's relationship fixup wires up whatever the change
        // tracker already holds — so that version passes by luck, and only in the call order the
        // handler happens to use.
        //
        // Five levels defeat it. GetById Includes exactly one level of ParentCategory, so walking
        // navigation from the deepest node reaches its parent and then stops: the grandparent is
        // untracked, its ParentCategory is null, and the walk reports a chain that does not contain
        // the category being moved. Only the persisted recursive read sees the whole thing.
        var root = await SeedCategoryAsync($"S5 deep root {Guid.NewGuid():N}");
        var l1 = await SeedCategoryAsync($"S5 deep l1 {Guid.NewGuid():N}", root);
        var l2 = await SeedCategoryAsync($"S5 deep l2 {Guid.NewGuid():N}", l1);
        var l3 = await SeedCategoryAsync($"S5 deep l3 {Guid.NewGuid():N}", l2);
        var l4 = await SeedCategoryAsync($"S5 deep l4 {Guid.NewGuid():N}", l3);

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{root}/parent",
            new MoveCategoryRequest { NewParentCategoryId = l4 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "moving a root beneath its own great-great-grandchild is a cycle however deep it is");
        (await StoredAsync(root))!.ParentCategoryId.Should().BeNull(
            "a cycle written to the database makes the whole subtree unreachable and the recursive "
            + "ancestor read loop forever");
    }

    [Test]
    public async Task Move_UnderItsDirectChild_Is400()
    {
        var root = await SeedCategoryAsync($"S5 direct root {Guid.NewGuid():N}");
        var child = await SeedCategoryAsync($"S5 direct child {Guid.NewGuid():N}", root);

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{root}/parent",
            new MoveCategoryRequest { NewParentCategoryId = child });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Move_ToItself_Is400()
    {
        var category = await SeedCategoryAsync($"S5 self {Guid.NewGuid():N}");

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{category}/parent",
            new MoveCategoryRequest { NewParentCategoryId = category });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Move_ToAMissingParent_Is400_AndChangesNothing()
    {
        var category = await SeedCategoryAsync($"S5 orphan {Guid.NewGuid():N}");

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{category}/parent",
            new MoveCategoryRequest { NewParentCategoryId = Guid.NewGuid() });

        // 400, not 404, and both halves of that are deliberate. ProblemForError infers 404 from a
        // ".NotFound" SUFFIX, and "Category.ParentNotFound" does not have one — the character before
        // "NotFound" is the "t" of "Parent". That near-miss is easy to read as a bug, but 400 is the
        // right answer anyway and is what CreateCategory already returns for the same code: the
        // resource the route names exists, and it is the request BODY that points at something
        // missing. Renaming the code to "Category.Parent.NotFound" to make the suffix match would
        // silently change CreateCategory's contract too.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Category.ParentNotFound");
        (await StoredAsync(category))!.ParentCategoryId.Should().BeNull();
    }

    [Test]
    public async Task Move_OfAMissingCategory_Is404()
    {
        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{Guid.NewGuid()}/parent",
            new MoveCategoryRequest { NewParentCategoryId = null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Category.NotFound");
    }

    #endregion

    #region Reorder

    [Test]
    public async Task Reorder_RewritesDisplayOrderWithinOneParent()
    {
        var parent = await SeedCategoryAsync($"S5 reorder parent {Guid.NewGuid():N}");
        var a = await SeedCategoryAsync($"S5 ra {Guid.NewGuid():N}", parent);
        var b = await SeedCategoryAsync($"S5 rb {Guid.NewGuid():N}", parent);
        var c = await SeedCategoryAsync($"S5 rc {Guid.NewGuid():N}", parent);

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/reorder",
            new ReorderCategoriesRequest { ParentCategoryId = parent, CategoryIds = [c, a, b] });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        (await StoredAsync(c))!.DisplayOrder.Should().Be(0);
        (await StoredAsync(a))!.DisplayOrder.Should().Be(1);
        (await StoredAsync(b))!.DisplayOrder.Should().Be(2);
    }

    [Test]
    public async Task Reorder_WithAPartialList_Is400_AndChangesNothing()
    {
        var parent = await SeedCategoryAsync($"S5 partial parent {Guid.NewGuid():N}");
        var a = await SeedCategoryAsync($"S5 pa {Guid.NewGuid():N}", parent);
        var b = await SeedCategoryAsync($"S5 pb {Guid.NewGuid():N}", parent);

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/reorder",
            new ReorderCategoriesRequest { ParentCategoryId = parent, CategoryIds = [b] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A half-applied reorder leaving duplicate positions is exactly what this endpoint replaced.
        (await StoredAsync(a))!.DisplayOrder.Should().Be(0);
        (await StoredAsync(b))!.DisplayOrder.Should().Be(0);
    }

    [Test]
    public async Task Reorder_WithADuplicateId_Is400()
    {
        var parent = await SeedCategoryAsync($"S5 dup parent {Guid.NewGuid():N}");
        var a = await SeedCategoryAsync($"S5 da {Guid.NewGuid():N}", parent);
        await SeedCategoryAsync($"S5 db {Guid.NewGuid():N}", parent);

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/reorder",
            new ReorderCategoriesRequest { ParentCategoryId = parent, CategoryIds = [a, a] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Reorder_WithACategoryFromAnotherParent_Is404()
    {
        var parent = await SeedCategoryAsync($"S5 foreign parent {Guid.NewGuid():N}");
        var mine = await SeedCategoryAsync($"S5 fa {Guid.NewGuid():N}", parent);
        var foreign = await SeedCategoryAsync($"S5 foreign {Guid.NewGuid():N}");

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/reorder",
            new ReorderCategoriesRequest { ParentCategoryId = parent, CategoryIds = [mine, foreign] });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Restore

    [Test]
    public async Task Restore_ReactivatesADeletedCategory()
    {
        var category = await SeedCategoryAsync($"S5 restorable {Guid.NewGuid():N}");
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{category}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"{CategoriesEndpoint}/{category}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the IsActive filter hides it everywhere");

        var response = await Client.PostAsync($"{CategoriesEndpoint}/{category}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await StoredAsync(category))!.IsActive.Should().BeTrue();
        (await Client.GetAsync($"{CategoriesEndpoint}/{category}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Restore_WhenTheSlugWasReused_Is409_AndChangesNothing()
    {
        // Both unique slug indexes are filtered on IsActive, so deleting frees the slug. Same shape
        // as Product.Restore's SKU collision.
        var slug = $"s5-slug-{Guid.NewGuid():N}";
        Guid original;
        using (var scope = Factory.Services.CreateScope())
        {
            original = await CatalogDataHelper.CreateCategoryAsync(
                scope.ServiceProvider, $"S5 original {Guid.NewGuid():N}", slug);
        }

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{original}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        using (var scope = Factory.Services.CreateScope())
        {
            await CatalogDataHelper.CreateCategoryAsync(
                scope.ServiceProvider, $"S5 took it {Guid.NewGuid():N}", slug);
        }

        var response = await Client.PostAsync($"{CategoriesEndpoint}/{original}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Category.SlugConflict");

        // The detail, not just the code: AddCategorySlugConflict() answers the same 409 with the
        // same code when the index catches the collision instead, so a code-only assertion would
        // pass with the handler's pre-check deleted. Same trap as Product.Restore's SKU check.
        problem.Detail.Should().Contain("already used by another category",
            "this must be answered by the handler's pre-check, not by the index's race backstop");

        (await StoredAsync(original))!.IsActive.Should().BeFalse("a refused restore must change nothing");
    }

    [Test]
    public async Task Restore_WhenTheParentIsStillDeleted_Is400()
    {
        // Restoring would produce a category that is active but unreachable from the root tree.
        var parent = await SeedCategoryAsync($"S5 dead parent {Guid.NewGuid():N}");
        var child = await SeedCategoryAsync($"S5 dead child {Guid.NewGuid():N}", parent);

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{child}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{parent}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await Client.PostAsync($"{CategoriesEndpoint}/{child}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Category.ParentNotActive");
    }

    [Test]
    public async Task Restore_OfALiveCategory_Is400()
    {
        var category = await SeedCategoryAsync($"S5 live {Guid.NewGuid():N}");

        var response = await Client.PostAsync($"{CategoriesEndpoint}/{category}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Category.NotDeleted");
    }

    #endregion

    #region A4 — includeInactive and its cache

    [Test]
    public async Task AnAdminSeesDeactivatedCategories_AndAnAnonymousCallerDoesNot()
    {
        var category = await SeedCategoryAsync($"S5 inactive {Guid.NewGuid():N}");
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{category}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var adminTree = await Client.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);
        adminTree!.Should().Contain(c => c.Id == category, "an admin sees the deactivated tree");

        using var anonymous = Factory.CreateClient();
        var publicTree = await anonymous.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);
        publicTree!.Should().NotContain(c => c.Id == category);
    }

    [Test]
    public async Task AnAdminRequest_DoesNotPoisonTheAnonymousResponse()
    {
        // A4 itself. The admin request runs FIRST, so if includeInactive were missing from the
        // cache key the anonymous caller would be served the admin's page out of Redis — a leak at
        // the cache layer that the API's own authorization would never allow.
        var category = await SeedCategoryAsync($"S5 poison {Guid.NewGuid():N}");
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{category}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var adminTree = await Client.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);
        adminTree!.Should().Contain(c => c.Id == category, "precondition: the admin entry is populated first");

        using var anonymous = Factory.CreateClient();
        var publicTree = await anonymous.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);

        publicTree!.Should().NotContain(c => c.Id == category,
            "the anonymous variant must have its own cache entry");
    }

    [Test]
    public async Task AnAnonymousRequest_DoesNotStarveTheAdminResponse()
    {
        // The mirror direction: priming the anonymous entry first must not hide deactivated
        // categories from the admin, which is what a shared key would do.
        var category = await SeedCategoryAsync($"S5 starve {Guid.NewGuid():N}");
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{category}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        using var anonymous = Factory.CreateClient();
        await anonymous.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);

        var adminTree = await Client.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);
        adminTree!.Should().Contain(c => c.Id == category);
    }

    [Test]
    public async Task ACategoryWrite_EvictsBothCacheVariants()
    {
        // The other half of A4: the family bump replaced three exact-key evictions of
        // "categories:all", which nothing writes any more. Without the family, both variants would
        // stay stale for the full ten-minute TTL and the eviction would log success.
        var name = $"S5 evict {Guid.NewGuid():N}";
        var category = await SeedCategoryAsync(name);

        using var anonymous = Factory.CreateClient();
        (await anonymous.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint))!
            .Should().Contain(c => c.Id == category, "precondition: anonymous entry primed");
        (await Client.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint))!
            .Should().Contain(c => c.Id == category, "precondition: admin entry primed");

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{category}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await anonymous.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint))!
            .Should().NotContain(c => c.Id == category, "the anonymous variant must have been evicted");
        (await Client.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint))!
            .Should().Contain(c => c.Id == category, "the admin variant must have been evicted and re-read, now showing it as deactivated");
    }

    #endregion

    #region Stats

    [Test]
    public async Task Stats_CountsProductsStockAndChildren()
    {
        var parent = await SeedCategoryAsync($"S5 stats {Guid.NewGuid():N}");
        await SeedCategoryAsync($"S5 stats child {Guid.NewGuid():N}", parent);

        using (var scope = Factory.Services.CreateScope())
        {
            await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Stats published", CatalogDataHelper.GenerateUniqueSku("ST1"), 10m, 5, parent);
            await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Stats draft", CatalogDataHelper.GenerateUniqueSku("ST2"), 10m, 0, parent, publish: false);
        }

        var stats = await Client.GetFromJsonAsync<CategoryStatsResponse>($"{CategoriesEndpoint}/{parent}/stats");

        stats!.CategoryId.Should().Be(parent);
        stats.ProductCount.Should().Be(2);
        stats.PublishedProductCount.Should().Be(1);
        stats.TotalStock.Should().Be(5);
        stats.OutOfStockCount.Should().Be(1);
        stats.ChildCategoryCount.Should().Be(1);
    }

    [Test]
    public async Task Stats_OnAnEmptyCategory_ReturnsZeros()
    {
        // A GroupBy over an empty set yields no rows, not a row of zeros — without the fallback
        // this would read as a missing category.
        var category = await SeedCategoryAsync($"S5 empty {Guid.NewGuid():N}");

        var stats = await Client.GetFromJsonAsync<CategoryStatsResponse>($"{CategoriesEndpoint}/{category}/stats");

        stats!.ProductCount.Should().Be(0);
        stats.TotalStock.Should().Be(0);
        stats.ChildCategoryCount.Should().Be(0);
    }

    [Test]
    public async Task Stats_ExcludesDeletedProducts()
    {
        var category = await SeedCategoryAsync($"S5 stats deleted {Guid.NewGuid():N}");
        Guid productId;
        using (var scope = Factory.Services.CreateScope())
        {
            productId = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "Stats deletable", CatalogDataHelper.GenerateUniqueSku("ST3"), 10m, 7, category);
        }

        (await Client.DeleteAsync($"/api/v1/products/{productId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var stats = await Client.GetFromJsonAsync<CategoryStatsResponse>($"{CategoriesEndpoint}/{category}/stats");

        stats!.ProductCount.Should().Be(0, "a deleted product is invisible everywhere else too");
        stats.TotalStock.Should().Be(0);
    }

    [Test]
    public async Task Stats_RefusesAnonymous()
    {
        var category = await SeedCategoryAsync($"S5 stats anon {Guid.NewGuid():N}");
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync($"{CategoriesEndpoint}/{category}/stats");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Stats_OfAMissingCategory_Is404()
    {
        var response = await Client.GetAsync($"{CategoriesEndpoint}/{Guid.NewGuid()}/stats");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region #54 — includeUnpublished on the by-category product list

    [Test]
    public async Task ByCategoryProducts_ShowDraftsToAnAdmin_ButNotToAnonymous()
    {
        var category = await SeedCategoryAsync($"S5 bycat {Guid.NewGuid():N}");
        Guid draftId;
        using (var scope = Factory.Services.CreateScope())
        {
            draftId = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "By-category draft", CatalogDataHelper.GenerateUniqueSku("BC1"), 10m, 1, category, publish: false);
        }

        var adminPage = await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{CategoriesEndpoint}/{category}/products?pageSize=100");
        adminPage!.Items.Should().Contain(p => p.Id == draftId);

        using var anonymous = Factory.CreateClient();
        var publicPage = await anonymous.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{CategoriesEndpoint}/{category}/products?pageSize=100");
        publicPage!.Items.Should().NotContain(p => p.Id == draftId);
    }

    [Test]
    public async Task ByCategoryProducts_AdminPage_DoesNotPoisonTheAnonymousOne()
    {
        // Same cache-key rule as A4, on the other endpoint the plan flagged (#54).
        var category = await SeedCategoryAsync($"S5 bycat poison {Guid.NewGuid():N}");
        Guid draftId;
        using (var scope = Factory.Services.CreateScope())
        {
            draftId = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "By-category poison", CatalogDataHelper.GenerateUniqueSku("BC2"), 10m, 1, category, publish: false);
        }

        (await Client.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{CategoriesEndpoint}/{category}/products?pageSize=100"))!
            .Items.Should().Contain(p => p.Id == draftId, "precondition: admin entry primed first");

        using var anonymous = Factory.CreateClient();
        var publicPage = await anonymous.GetFromJsonAsync<PagedResponse<ProductResponse>>(
            $"{CategoriesEndpoint}/{category}/products?pageSize=100");

        publicPage!.Items.Should().NotContain(p => p.Id == draftId,
            "the visibility flag must be part of the cache key");
    }

    #endregion
}
