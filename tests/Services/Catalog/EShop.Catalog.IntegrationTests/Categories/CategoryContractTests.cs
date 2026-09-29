using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Categories;

/// <summary>
/// Frontend-contracts R5: the category findings F-37 (tree depth), F-38 (move body), F-39 (slugs),
/// F-40 (status codes) and F-43 (restoring a product into a deleted category). Every test goes
/// through HTTP, so caching, binding and the status mapping are all in play.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CategoryContractTests : AuthenticatedIntegrationTestBase
{
    private const string CategoriesEndpoint = "/api/v1/categories";

    private async Task<Guid> SeedAsync(string name, Guid? parentId = null, string? slug = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, $"{name} {Guid.NewGuid():N}", slug, parentId);
    }

    /// <summary>A root and four levels below it — two deeper than the old Include chain reached.</summary>
    private async Task<Guid[]> SeedChainAsync(string prefix)
    {
        var ids = new Guid[5];
        ids[0] = await SeedAsync($"{prefix} L1");
        for (var level = 1; level < ids.Length; level++)
            ids[level] = await SeedAsync($"{prefix} L{level + 1}", ids[level - 1]);
        return ids;
    }

    private async Task<Domain.Entities.Category?> StoredAsync(Guid categoryId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Categories.AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.Id == categoryId);
    }

    private async Task<CategoryResponse> DetailAsync(Guid id, HttpClient? client = null)
    {
        using var response = await (client ?? Client).GetAsync($"{CategoriesEndpoint}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private static async Task<ProblemDetailsResponse> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!;
    }

    /// <summary>Walks single-child links down from <paramref name="node"/>, returning each id.</summary>
    private static List<Guid> Spine(CategoryResponse node)
    {
        var ids = new List<Guid> { node.Id };
        while (node.ChildCategories is [var only])
        {
            node = only;
            ids.Add(node.Id);
        }

        return ids;
    }

    #region F-37 — the tree at any depth

    [Test]
    public async Task TheTree_ShowsEveryLevel_AndTheDeepestIsARealLeaf()
    {
        var chain = await SeedChainAsync("F37 tree");

        using var anonymous = Factory.CreateClient();
        var tree = await anonymous.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);

        var root = tree!.Single(c => c.Id == chain[0]);
        Spine(root).Should().Equal(chain, "the old tree stopped at the grandchild (the third id) and showed it as a leaf");
    }

    [Test]
    public async Task TheDetail_ShowsTheWholeSubtree_WithTheParentsName()
    {
        var chain = await SeedChainAsync("F37 detail");

        var detail = await DetailAsync(chain[1]);

        Spine(detail).Should().Equal(chain[1..], "the old detail showed one level of children, each with childCategories: []");
        detail.ParentCategoryName.Should().StartWith("F37 detail L1");
        detail.ChildCategories!.Single().ParentCategoryName.Should().StartWith("F37 detail L2");
    }

    /// <summary>
    /// The detail embeds the whole subtree, so renaming a category four levels down must reach the
    /// root's cached entry. The M8 exact-key evictions reached only the parent and children — this is
    /// what moving the detail into the versioned family is for. The first read is what caches it.
    /// </summary>
    [Test]
    public async Task AnAncestorsCachedDetail_ShowsARenameDeepBelowIt()
    {
        var chain = await SeedChainAsync("F37 cache");
        (await DetailAsync(chain[0])).ChildCategories.Should().NotBeEmpty();

        var renamed = $"F37 renamed {Guid.NewGuid():N}";
        using var update = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{chain[3]}",
            new UpdateCategoryRequest { Id = chain[3], Name = renamed });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent, await update.Content.ReadAsStringAsync());

        var level4 = (await DetailAsync(chain[0])).ChildCategories!.Single().ChildCategories!.Single().ChildCategories!.Single();
        level4.Name.Should().Be(renamed, "a stale root entry would still show the old name for five minutes");
    }

    /// <summary>
    /// The admin tree lifts the IsActive filter for the whole read, so a deleted category shows at
    /// any depth under its live parent — and only to the admin.
    /// </summary>
    [Test]
    public async Task ADeletedLeafFourLevelsDown_ShowsInTheAdminTreeOnly()
    {
        var chain = await SeedChainAsync("F37 admin");
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{chain[4]}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var adminTree = await Client.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);
        var adminLeaf = adminTree!.Single(c => c.Id == chain[0])
            .ChildCategories!.Single().ChildCategories!.Single().ChildCategories!.Single().ChildCategories!.Single();
        adminLeaf.Id.Should().Be(chain[4]);
        adminLeaf.IsActive.Should().BeFalse();

        using var anonymous = Factory.CreateClient();
        var publicTree = await anonymous.GetFromJsonAsync<List<CategoryResponse>>(CategoriesEndpoint);
        Spine(publicTree!.Single(c => c.Id == chain[0])).Should().Equal(chain[..4]);
    }

    #endregion

    #region F-38 — the move body must carry the property

    [Test]
    public async Task Move_WithAnEmptyObject_IsA400KeyedToTheField_AndChangesNothing()
    {
        var parent = await SeedAsync("F38 parent");
        var child = await SeedAsync("F38 child", parent);

        using var response = await Client.PutAsync($"{CategoriesEndpoint}/{child}/parent",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        problem.ErrorCode.Should().Be("ValidationError");
        problem.Errors.Should().ContainKey("newParentCategoryId");
        (await StoredAsync(child))!.ParentCategoryId.Should().Be(parent, "{} used to re-root the category");
    }

    [Test]
    public async Task Move_WithAnExplicitNull_StillPromotesToRoot()
    {
        var parent = await SeedAsync("F38 null parent");
        var child = await SeedAsync("F38 null child", parent);

        using var response = await Client.PutAsync($"{CategoriesEndpoint}/{child}/parent",
            new StringContent("""{"newParentCategoryId":null}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await StoredAsync(child))!.ParentCategoryId.Should().BeNull();
    }

    [Test]
    public async Task Move_WithNoBody_IsStillAMalformedRequest()
    {
        var child = await SeedAsync("F38 no body");

        using var response = await Client.PutAsync($"{CategoriesEndpoint}/{child}/parent",
            new StringContent("", Encoding.UTF8, "application/json"));

        (await ProblemAsync(response, HttpStatusCode.BadRequest)).ErrorCode.Should().Be("MalformedRequest");
    }

    /// <summary>
    /// The presence flag is <c>[JsonIgnore]</c> with a private setter, so a body naming it is read as
    /// a body without the real property. (System.Text.Json skips an ignored member's name rather than
    /// treating it as unmapped, so this is not a <c>MalformedRequest</c>.)
    /// </summary>
    [Test]
    public async Task Move_CannotSmuggleThePresenceFlag()
    {
        var parent = await SeedAsync("F38 smuggle parent");
        var child = await SeedAsync("F38 smuggle child", parent);

        using var response = await Client.PutAsync($"{CategoriesEndpoint}/{child}/parent",
            new StringContent("""{"hasNewParentCategoryId":true}""", Encoding.UTF8, "application/json"));

        (await ProblemAsync(response, HttpStatusCode.BadRequest)).Errors.Should().ContainKey("newParentCategoryId");
        (await StoredAsync(child))!.ParentCategoryId.Should().Be(parent);
    }

    /// <summary>
    /// The OpenAPI document must say what the server enforces: the property is required AND nullable.
    /// The positional record this replaced was listed as required; the class lost that until it took
    /// <c>[Required]</c>, which a generated client would otherwise read as "may be omitted".
    /// </summary>
    [Test]
    public async Task TheOpenApiDocument_ListsTheMoveProperty_AsRequiredAndNullable()
    {
        using var anonymous = Factory.CreateClient();
        using var document = JsonDocument.Parse(await anonymous.GetStringAsync("/openapi/v1.json"));
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("MoveCategoryRequest");

        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("newParentCategoryId");
        schema.GetProperty("properties").GetProperty("newParentCategoryId").GetProperty("type").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("null");
    }

    #endregion

    #region F-39 — slugs

    [TestCase("Mixed Case Slug!!")]
    [TestCase("  padded  ")]
    [TestCase("under_score")]
    public async Task Create_WithAMalformedSlug_IsA400KeyedSlug(string slug)
    {
        using var response = await Client.PostAsJsonAsync(CategoriesEndpoint,
            new CreateCategoryRequest { Name = $"F39 bad {Guid.NewGuid():N}", Slug = slug });

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        problem.ErrorCode.Should().Be("ValidationError");
        problem.Errors.Should().ContainKey("slug");
    }

    [Test]
    public async Task Update_ChangesTheSlug()
    {
        var id = await SeedAsync("F39 change");
        var slug = $"f39-new-{Guid.NewGuid():N}";

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}",
            new UpdateCategoryRequest { Id = id, Name = "F39 change", Slug = slug });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await DetailAsync(id)).Slug.Should().Be(slug);
    }

    [Test]
    public async Task Update_WithoutASlug_KeepsIt()
    {
        var slug = $"f39-keep-{Guid.NewGuid():N}";
        var id = await SeedAsync("F39 keep", slug: slug);

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}",
            new UpdateCategoryRequest { Id = id, Name = "F39 keep renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StoredAsync(id))!.Slug.Should().Be(slug);
    }

    [Test]
    public async Task Update_ResendingItsOwnSlug_IsAllowed()
    {
        var slug = $"f39-own-{Guid.NewGuid():N}";
        var id = await SeedAsync("F39 own", slug: slug);

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}",
            new UpdateCategoryRequest { Id = id, Name = "F39 own", Slug = slug });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "a category must not collide with itself");
    }

    [Test]
    public async Task Update_ToASlugASiblingHolds_Is409_AndChangesNothing()
    {
        var parent = await SeedAsync("F39 siblings");
        var taken = $"f39-taken-{Guid.NewGuid():N}";
        await SeedAsync("F39 holder", parent, taken);
        var id = await SeedAsync("F39 mover", parent);
        var before = (await StoredAsync(id))!;

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}",
            new UpdateCategoryRequest { Id = id, Name = "F39 should not stick", Slug = taken });

        var problem = await ProblemAsync(response, HttpStatusCode.Conflict);
        problem.ErrorCode.Should().Be("Category.SlugConflict");
        problem.Detail.Should().Contain("already exists at this level", "the pre-check answers, not the index");
        var after = (await StoredAsync(id))!;
        after.Slug.Should().Be(before.Slug);
        after.Name.Should().Be(before.Name, "TransactionBehavior commits a failure Result, so nothing may be mutated first");
    }

    [Test]
    public async Task Update_ToTheSlugOfACategoryAtAnotherLevel_IsAllowed()
    {
        var taken = $"f39-elsewhere-{Guid.NewGuid():N}";
        await SeedAsync("F39 other level", await SeedAsync("F39 other parent"), taken);
        var id = await SeedAsync("F39 root level");

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}",
            new UpdateCategoryRequest { Id = id, Name = "F39 root level", Slug = taken });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "slugs are unique per level, not globally");
    }

    [TestCase("")]
    [TestCase("Bad Slug")]
    public async Task Update_WithAMalformedSlug_IsA400KeyedSlug(string slug)
    {
        var id = await SeedAsync("F39 malformed");

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}",
            new UpdateCategoryRequest { Id = id, Name = "F39 malformed", Slug = slug });

        (await ProblemAsync(response, HttpStatusCode.BadRequest)).Errors.Should().ContainKey("slug");
    }

    /// <summary>
    /// The move used to reach the unique index and answer "created concurrently … retry" — nothing was
    /// concurrent and no retry could succeed. Now the pre-check answers, naming a fix that exists, and
    /// following that fix makes the same move succeed.
    /// </summary>
    [Test]
    public async Task Move_IntoALevelWhereTheSlugIsTaken_Is409WithAFixThatWorks()
    {
        var slug = $"f39-clash-{Guid.NewGuid():N}";
        var target = await SeedAsync("F39 target");
        await SeedAsync("F39 resident", target, slug);
        var mover = await SeedAsync("F39 mover", slug: slug);

        using var refused = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{mover}/parent",
            new MoveCategoryRequest { NewParentCategoryId = target });

        var problem = await ProblemAsync(refused, HttpStatusCode.Conflict);
        problem.ErrorCode.Should().Be("Category.SlugConflict");
        problem.Detail.Should().Contain("Change this category's slug").And.NotContain("concurrently");
        (await StoredAsync(mover))!.ParentCategoryId.Should().BeNull();

        using var rename = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{mover}",
            new UpdateCategoryRequest { Id = mover, Name = "F39 mover", Slug = $"{slug}-2" });
        rename.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var moved = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{mover}/parent",
            new MoveCategoryRequest { NewParentCategoryId = target });
        moved.StatusCode.Should().Be(HttpStatusCode.NoContent, await moved.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The move's slug check applies only when the level changes: at its own level the category
    /// holds its own slug, and checking would make it collide with itself.
    /// </summary>
    [Test]
    public async Task Move_ToItsCurrentParent_IsANoOp()
    {
        var parent = await SeedAsync("F39 same level");
        var child = await SeedAsync("F39 stays", parent, $"f39-stays-{Guid.NewGuid():N}");

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{child}/parent",
            new MoveCategoryRequest { NewParentCategoryId = parent });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await StoredAsync(child))!.ParentCategoryId.Should().Be(parent);
    }

    /// <summary>
    /// Restore's conflict told the admin to change the other category's slug when no endpoint could.
    /// Now one can, and doing so is what lets the restore through.
    /// </summary>
    [Test]
    public async Task Restore_BlockedByASlug_SucceedsOnceTheOtherCategorysSlugChanges()
    {
        var slug = $"f39-restore-{Guid.NewGuid():N}";
        var original = await SeedAsync("F39 original", slug: slug);
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{original}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var usurper = await SeedAsync("F39 usurper", slug: slug);

        using (var refused = await Client.PostAsync($"{CategoriesEndpoint}/{original}/restore", null))
            (await ProblemAsync(refused, HttpStatusCode.Conflict)).ErrorCode.Should().Be("Category.SlugConflict");

        using (var rename = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{usurper}",
                   new UpdateCategoryRequest { Id = usurper, Name = "F39 usurper", Slug = $"{slug}-b" }))
            rename.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var restored = await Client.PostAsync($"{CategoriesEndpoint}/{original}/restore", null);
        restored.StatusCode.Should().Be(HttpStatusCode.NoContent, await restored.Content.ReadAsStringAsync());
    }

    #endregion

    #region F-40 — status codes

    [Test]
    public async Task Delete_WithLiveChildren_Is409_AndTheCategoryStays()
    {
        var parent = await SeedAsync("F40 parent");
        await SeedAsync("F40 child", parent);

        using var response = await Client.DeleteAsync($"{CategoriesEndpoint}/{parent}");

        (await ProblemAsync(response, HttpStatusCode.Conflict)).ErrorCode.Should().Be("Category.HasChildren");
        (await StoredAsync(parent))!.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task Get_TheAllZeroId_IsA400ValidationError()
    {
        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"{CategoriesEndpoint}/{Guid.Empty}");

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        problem.ErrorCode.Should().Be("ValidationError");
        problem.Errors.Should().ContainKey("id");
    }

    [Test]
    public async Task Get_AnUnknownId_IsStillA404()
    {
        using var anonymous = Factory.CreateClient();
        using var response = await anonymous.GetAsync($"{CategoriesEndpoint}/{Guid.NewGuid()}");

        (await ProblemAsync(response, HttpStatusCode.NotFound)).ErrorCode.Should().Be("Category.NotFound");
    }

    [Test]
    public async Task Move_ToAMissingParent_IsStillA400()
    {
        var child = await SeedAsync("F40 orphan-to-be");

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{child}/parent",
            new MoveCategoryRequest { NewParentCategoryId = Guid.NewGuid() });

        (await ProblemAsync(response, HttpStatusCode.BadRequest)).ErrorCode.Should().Be("Category.ParentNotFound",
            "the body points at nothing; 404 is reserved for the category the route names");
    }

    #endregion

    #region F-43 — restoring a product into a deleted category

    [Test]
    public async Task RestoringAProductIntoADeletedCategory_Is400_AndItStaysInTheRecycleBin()
    {
        var category = await SeedAsync("F43 category");
        Guid product;
        using (var scope = Factory.Services.CreateScope())
        {
            product = await CatalogDataHelper.CreateProductAsync(
                scope.ServiceProvider, "F43 product", CatalogDataHelper.GenerateUniqueSku("F43"), 10m, 1, category);
        }

        (await Client.DeleteAsync($"/api/v1/products/{product}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{category}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent, "a deleted product does not block its category's delete");

        using (var refused = await Client.PostAsync($"/api/v1/products/{product}/restore", null))
        {
            var problem = await ProblemAsync(refused, HttpStatusCode.BadRequest);
            problem.ErrorCode.Should().Be("Product.CategoryNotActive");
            problem.Detail.Should().Contain($"/api/v1/categories/{category}/restore");
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            (await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == product)).IsDeleted
                .Should().BeTrue("the old restore answered 204 and left the product unreachable");
        }

        (await Client.PostAsync($"{CategoriesEndpoint}/{category}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var restored = await Client.PostAsync($"/api/v1/products/{product}/restore", null);
        restored.StatusCode.Should().Be(HttpStatusCode.NoContent, await restored.Content.ReadAsStringAsync());
        (await Client.GetAsync($"/api/v1/products/{product}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}

/// <summary>
/// F-37: the tree used to <c>Take(100)</c> roots. A fixture of its own, so its hundred-odd roots
/// neither slow nor disturb the other category tests' shared database.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CategoryTreeRootCountTests : AuthenticatedIntegrationTestBase
{
    [Test]
    public async Task TheTree_ReturnsEveryRoot_NotJustTheFirstHundred()
    {
        var marker = $"F37 root {Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            for (var i = 0; i < 120; i++)
                db.Categories.Add(Domain.Entities.Category.Create($"{marker} {i:D3}", null, null));
            await db.SaveChangesAsync();
        }

        // First read of the tree in this host, so nothing cached before the rows existed.
        using var anonymous = Factory.CreateClient();
        var tree = await anonymous.GetFromJsonAsync<List<CategoryResponse>>("/api/v1/categories");

        tree!.Count(c => c.Name.StartsWith(marker)).Should().Be(120);
    }
}
