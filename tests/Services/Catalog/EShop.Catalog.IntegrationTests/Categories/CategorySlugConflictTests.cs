using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Fixtures;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Catalog.IntegrationTests.Categories;

/// <summary>
/// M9 / M12 (Catalog audit Stage 8), the database side. With the handler's slug pre-check blinded
/// (<see cref="BlindSlugCheckApiFactory"/>), only the two IsActive-filtered unique indexes stand
/// between a request and a duplicate slug — so these pin what the indexes enforce and how a
/// violation is reported, independently of the pre-check that normally hides both.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class CategorySlugConflictTests : AuthenticatedIntegrationTestBase
{
    private const string CategoriesEndpoint = "/api/v1/categories";

    protected override async Task<CatalogApiFactory> CreateFactoryAsync()
        => await BlindSlugCheckApiFactory.CreateAsync();

    private static string UniqueSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private Task<HttpResponseMessage> PostAsync(string name, string slug, Guid? parentId = null)
        => Client.PostAsJsonAsync(CategoriesEndpoint,
            new CreateCategoryRequest { Name = name, Slug = slug, ParentCategoryId = parentId });

    private async Task<Guid> CreateAsync(string name, string slug, Guid? parentId = null)
    {
        using var response = await PostAsync(name, slug, parentId);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private static async Task ShouldBeASlugConflict(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!;
        problem.ErrorCode.Should().Be("Category.SlugConflict",
            "AddCategorySlugConflict must claim the violation before AddEfDuplicateKey reports a generic DuplicateResource");

        // F-39 (frontend-contracts R5): a slug change, a move and a restore can race here too, so the
        // detail no longer says the other category was "created" or tells a restore to pick a new slug.
        problem.Detail.Should().Be("Another category at this level took the same slug concurrently. Reload the categories and try again.");
    }

    /// <summary>The root index, <c>IX_Categories_Slug</c>.</summary>
    [Test]
    public async Task ADuplicateRootSlugReachingTheIndex_IsA409SlugConflict()
    {
        var slug = UniqueSlug("root");
        await CreateAsync("First", slug);

        using var response = await PostAsync("Second", slug);

        await ShouldBeASlugConflict(response);
    }

    /// <summary>The per-parent index, <c>IX_Categories_ParentCategoryId_Slug</c>.</summary>
    [Test]
    public async Task ADuplicateChildSlugReachingTheIndex_IsA409SlugConflict()
    {
        var parent = await CreateAsync("Parent", UniqueSlug("parent"));
        var slug = UniqueSlug("child");
        await CreateAsync("First", slug, parent);

        using var response = await PostAsync("Second", slug, parent);

        await ShouldBeASlugConflict(response);
    }

    /// <summary>
    /// F-39 (frontend-contracts R5). A slug change is a second writer that can race; when it loses,
    /// the index answers with the same code, and a detail that no longer assumes a create.
    /// </summary>
    [Test]
    public async Task ASlugChangeReachingTheIndex_IsA409SlugConflict()
    {
        var taken = UniqueSlug("taken");
        await CreateAsync("Holder", taken);
        var id = await CreateAsync("Changer", UniqueSlug("changer"));

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}",
            new UpdateCategoryRequest { Id = id, Name = "Changer", Slug = taken });

        await ShouldBeASlugConflict(response);
    }

    /// <summary>F-39. The move's pre-check blinded, the same clash reaches the index as a race would.</summary>
    [Test]
    public async Task AMoveReachingTheIndex_IsA409SlugConflict()
    {
        var slug = UniqueSlug("moved");
        var target = await CreateAsync("Target", UniqueSlug("target"));
        await CreateAsync("Resident", slug, target);
        var mover = await CreateAsync("Mover", slug);

        using var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{mover}/parent",
            new MoveCategoryRequest { NewParentCategoryId = target });

        await ShouldBeASlugConflict(response);
    }

    /// <summary>
    /// M12. With the pre-check blinded, only the index decides — so this passes only because the
    /// migration filtered it on IsActive. Unfiltered, the deleted row would still hold the slug.
    /// </summary>
    [Test]
    public async Task ASoftDeletedCategorysSlug_IsAcceptedByTheIndex()
    {
        var slug = UniqueSlug("reuse");
        var id = await CreateAsync("Deleted", slug);
        (await Client.DeleteAsync($"{CategoriesEndpoint}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var response = await PostAsync("Replacement", slug);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
