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
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode.Should().Be("Category.SlugConflict",
            "AddCategorySlugConflict must claim the violation before AddEfDuplicateKey reports a generic DuplicateResource");
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
