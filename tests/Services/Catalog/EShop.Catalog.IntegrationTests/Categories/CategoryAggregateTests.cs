using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Categories;

/// <summary>
/// Catalog audit Stage 8 — the Category aggregate, end to end over HTTP on real Postgres.
///
/// <para>
/// Cache tests must go through HTTP: a handler-level test bypasses <c>CachingBehavior</c> and passes
/// regardless. Each one reads first (so the entry is cached), writes, then reads again.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class CategoryAggregateTests : AuthenticatedIntegrationTestBase
{
    private const string CategoriesEndpoint = "/api/v1/categories";

    private static string UniqueSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private Task<HttpResponseMessage> PostAsync(CreateCategoryRequest request)
        => Client.PostAsJsonAsync(CategoriesEndpoint, request);

    private async Task<Guid> CreateAsync(
        string name, Guid? parentId = null, string? description = null, int? displayOrder = null)
    {
        using var response = await PostAsync(new CreateCategoryRequest
        {
            Name = name,
            Slug = UniqueSlug("cat"),
            ParentCategoryId = parentId,
            Description = description,
            DisplayOrder = displayOrder
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private async Task<CategoryResponse> GetAsync(Guid id)
    {
        using var response = await Client.GetAsync($"{CategoriesEndpoint}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private Task<HttpResponseMessage> PutAsync(Guid id, string name, string? description = null, int? displayOrder = null)
        => Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}", new UpdateCategoryRequest
        {
            Id = id,
            Name = name,
            Description = description,
            DisplayOrder = displayOrder
        });

    private static IEnumerable<CategoryResponse> Children(CategoryResponse category)
        => category.ChildCategories ?? [];

    #region M8 — a parent's cached detail embeds its children

    [Test]
    public async Task CreatingAChild_ShowsUpInTheParentsAlreadyCachedDetail()
    {
        var parent = await CreateAsync("M8 Parent");
        Children(await GetAsync(parent)).Should().BeEmpty();

        var child = await CreateAsync("M8 Child", parent);

        Children(await GetAsync(parent)).Select(c => c.Id).Should().Contain(child,
            "creating a child must evict category:{parentId}, or the parent's detail stays stale for its TTL");
    }

    [Test]
    public async Task RenamingAChild_ShowsUpInTheParentsAlreadyCachedDetail()
    {
        var parent = await CreateAsync("M8 Rename Parent");
        var child = await CreateAsync("Before", parent);
        Children(await GetAsync(parent)).Single().Name.Should().Be("Before");

        (await PutAsync(child, "After")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Children(await GetAsync(parent)).Single().Name.Should().Be("After");
    }

    [Test]
    public async Task DeletingAChild_RemovesItFromTheParentsAlreadyCachedDetail()
    {
        var parent = await CreateAsync("M8 Delete Parent");
        var child = await CreateAsync("Doomed", parent);
        Children(await GetAsync(parent)).Should().ContainSingle();

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{child}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Children(await GetAsync(parent)).Should().BeEmpty();
    }

    /// <summary>
    /// The reverse direction: every child's detail embeds <c>ParentCategoryName</c>, so renaming a
    /// parent must evict each child's entry too.
    /// </summary>
    [Test]
    public async Task RenamingAParent_ShowsUpInItsChildsAlreadyCachedDetail()
    {
        var parent = await CreateAsync("Old Parent Name");
        var child = await CreateAsync("M8 Named Child", parent);
        (await GetAsync(child)).ParentCategoryName.Should().Be("Old Parent Name");

        (await PutAsync(parent, "New Parent Name")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetAsync(child)).ParentCategoryName.Should().Be("New Parent Name");
    }

    #endregion

    #region M9 — slugs

    /// <summary>
    /// A name with no Latin letters used to produce an empty slug, and the second such root then
    /// collided on the unique root-slug index as a generic 409 <c>DuplicateResource</c>.
    /// </summary>
    [Test]
    public async Task NamesWithNoLatinLetters_StillGetDistinctUsableSlugs()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            using var response = await PostAsync(new CreateCategoryRequest { Name = "Книги", Slug = null });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            ids.Add((await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id);
        }

        var slugs = new List<string>();
        foreach (var id in ids)
        {
            slugs.Add((await GetAsync(id)).Slug);
        }

        slugs.Should().OnlyContain(s => s.StartsWith("category-"));
        slugs.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public async Task ADuplicateSlugUnderOneParent_IsAnExplicitSlugConflict()
    {
        var parent = await CreateAsync("M9 Parent");
        var slug = UniqueSlug("dup");

        using (var first = await PostAsync(new CreateCategoryRequest { Name = "One", Slug = slug, ParentCategoryId = parent }))
        {
            first.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var second = await PostAsync(new CreateCategoryRequest { Name = "Two", Slug = slug, ParentCategoryId = parent });

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Category.SlugConflict");
    }

    [Test]
    public async Task TheSameSlugUnderDifferentParents_IsAllowed()
    {
        var slug = UniqueSlug("shared");
        var firstParent = await CreateAsync("M9 Parent A");
        var secondParent = await CreateAsync("M9 Parent B");

        using var first = await PostAsync(new CreateCategoryRequest { Name = "X", Slug = slug, ParentCategoryId = firstParent });
        using var second = await PostAsync(new CreateCategoryRequest { Name = "X", Slug = slug, ParentCategoryId = secondParent });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// The pre-check is read-then-write, so concurrent creates can all pass it; the unique index
    /// decides, and the loser must be told <c>Category.SlugConflict</c> (409, retryable), not the
    /// generic <c>DuplicateResource</c> that every other unique violation maps to.
    /// </summary>
    [Test]
    public async Task ConcurrentCreatesWithOneSlug_YieldExactlyOneCategory()
    {
        var slug = UniqueSlug("race");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            PostAsync(new CreateCategoryRequest { Name = $"Racer {i}", Slug = slug })));

        try
        {
            responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);

            foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                loser.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
                (await loser.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
                    .Should().Be("Category.SlugConflict");
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    #endregion

    #region M10 / M11 — description and display order

    [Test]
    public async Task CreateStoresTheDescriptionAndDisplayOrder()
    {
        var id = await CreateAsync("M11 Described", description: "  Kept  ", displayOrder: 7);

        var category = await GetAsync(id);

        category.Description.Should().Be("Kept");
        category.DisplayOrder.Should().Be(7);
    }

    /// <summary>
    /// M10, the BUG-09 shape: a PUT carrying only a name used to wipe the description.
    /// </summary>
    [Test]
    public async Task AnUpdateThatOmitsTheDescription_KeepsIt()
    {
        var id = await CreateAsync("M10 Keep", description: "Keep me");

        (await PutAsync(id, "M10 Keep Renamed")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var category = await GetAsync(id);
        category.Name.Should().Be("M10 Keep Renamed");
        category.Description.Should().Be("Keep me");
    }

    [Test]
    public async Task AnEmptyDescription_ClearsIt()
    {
        var id = await CreateAsync("M10 Clear", description: "Remove me");

        (await PutAsync(id, "M10 Clear", description: "")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetAsync(id)).Description.Should().BeNull();
    }

    [Test]
    public async Task ChildrenAreOrderedByDisplayOrderThenName()
    {
        var parent = await CreateAsync("M11 Ordered Parent");
        await CreateAsync("Bravo", parent, displayOrder: 0);
        await CreateAsync("Aardvark", parent, displayOrder: 1);
        await CreateAsync("Alpha", parent, displayOrder: 0);

        Children(await GetAsync(parent)).Select(c => c.Name)
            .Should().Equal("Alpha", "Bravo", "Aardvark");
    }

    /// <summary>
    /// Every seeded root has DisplayOrder 0, so ordering by DisplayOrder alone left their order to
    /// Postgres — and the result was cached for ten minutes under <c>categories:all</c>.
    /// </summary>
    [Test]
    public async Task RootsWithEqualDisplayOrder_AreOrderedByName()
    {
        using var response = await Client.GetAsync(CategoriesEndpoint);
        var roots = (await response.Content.ReadFromJsonAsync<List<CategoryResponse>>())!;

        roots.Select(r => r.Name).Where(n => n is "Books" or "Clothing" or "Electronics")
            .Should().Equal("Books", "Clothing", "Electronics");
    }

    #endregion

    #region M12 — soft delete

    [Test]
    public async Task ADeletedCategory_Disappears_ButItsRowRemainsInactive()
    {
        var id = await CreateAsync("M12 Soft");

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"{CategoriesEndpoint}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var row = await db.Categories.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
        row.IsActive.Should().BeFalse("delete is soft now; it used to be a hard, irreversible Remove");
    }

    [Test]
    public async Task ADeletedRootsSlug_CanBeReused()
    {
        var slug = UniqueSlug("reuse");
        using var first = await PostAsync(new CreateCategoryRequest { Name = "Reuse", Slug = slug });
        var id = (await first.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var second = await PostAsync(new CreateCategoryRequest { Name = "Reuse", Slug = slug });
        second.StatusCode.Should().Be(HttpStatusCode.Created,
            "the slug indexes are filtered on IsActive, so a soft-deleted category no longer holds its slug");
    }

    [Test]
    public async Task ACategoryWhoseOnlyChildWasDeleted_CanBeDeleted()
    {
        var parent = await CreateAsync("M12 Parent");
        var child = await CreateAsync("M12 Child", parent);

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{child}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client.DeleteAsync($"{CategoriesEndpoint}/{parent}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task ADeletedParent_CannotReceiveChildren()
    {
        var parent = await CreateAsync("M12 Gone Parent");
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{parent}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var response = await PostAsync(new CreateCategoryRequest
        {
            Name = "Orphan",
            Slug = UniqueSlug("orphan"),
            ParentCategoryId = parent
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Category.ParentNotFound");
    }

    #endregion
}
