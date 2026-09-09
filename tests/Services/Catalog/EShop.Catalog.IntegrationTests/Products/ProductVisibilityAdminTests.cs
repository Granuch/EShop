using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// D1 / H5a, admin side: the publish/unpublish lifecycle, and the fact that an admin can see drafts
/// at all.
///
/// <para>
/// That last part is not a convenience. With the read filter in place and no admin exemption, a
/// product would be invisible to the very person who just created it — <c>POST</c> returns 201 with
/// an id and the follow-up <c>GET</c> on that id returns 404 — so there would be no way to review a
/// product before publishing it.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductVisibilityAdminTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<Guid> CreateDraftViaApiAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var response = await Client.PostAsJsonAsync(ProductsEndpoint, new CreateProductRequest
        {
            Name = "Freshly Created Product",
            Sku = CatalogDataHelper.GenerateUniqueSku("PUB"),
            Price = 15m,
            StockQuantity = 3,
            CategoryId = categoryId
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    [Test]
    public async Task ANewlyCreatedProduct_IsDraftAndVisibleToItsAdminCreator()
    {
        var id = await CreateDraftViaApiAsync();

        var response = await Client.GetAsync($"{ProductsEndpoint}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an admin must be able to review a product before publishing it");

        var product = await response.Content.ReadFromJsonAsync<ProductDetailsResponse>();
        product!.Status.Should().Be(ProductStatus.Draft);
    }

    [Test]
    public async Task PublishingAProduct_MakesItActive()
    {
        var id = await CreateDraftViaApiAsync();

        (await Client.PostAsync($"{ProductsEndpoint}/{id}/publish", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var product = await (await Client.GetAsync($"{ProductsEndpoint}/{id}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();

        product!.Status.Should().Be(ProductStatus.Active);
    }

    [Test]
    public async Task UnpublishingAPublishedProduct_ReturnsItToDraft()
    {
        var id = await CreateDraftViaApiAsync();
        await Client.PostAsync($"{ProductsEndpoint}/{id}/publish", null);

        (await Client.PostAsync($"{ProductsEndpoint}/{id}/unpublish", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var product = await (await Client.GetAsync($"{ProductsEndpoint}/{id}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();

        product!.Status.Should().Be(ProductStatus.Draft);
    }

    /// <summary>
    /// Both commands are idempotent on purpose. <c>Product.Publish</c> throws for a non-Draft
    /// product and a <c>DomainException</c> surfaces as 400, which is right for a Discontinued
    /// product but wrong for a retried request — a client that retries after a dropped response
    /// would get an error for an operation that had already succeeded.
    /// </summary>
    [Test]
    public async Task PublishingTwice_IsANoOpRatherThanAnError()
    {
        var id = await CreateDraftViaApiAsync();

        (await Client.PostAsync($"{ProductsEndpoint}/{id}/publish", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await Client.PostAsync($"{ProductsEndpoint}/{id}/publish", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task UnpublishingADraft_IsANoOpRatherThanAnError()
    {
        var id = await CreateDraftViaApiAsync();

        (await Client.PostAsync($"{ProductsEndpoint}/{id}/unpublish", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task PublishingAMissingProduct_Is404()
    {
        (await Client.PostAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/publish", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Publishing changes which products appear in a list, so it must invalidate the same cache a
    /// price change does. Without the family bump the newly published product would stay absent
    /// from any already-cached public list for the full 5-minute TTL — the DEBT-16 symptom, in a
    /// new place.
    /// </summary>
    [Test]
    public async Task PublishingAProduct_InvalidatesTheCachedPublicList()
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var sku = CatalogDataHelper.GenerateUniqueSku("CACHEPUB");

        var id = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Cache Publish Probe", sku, 11m, 2, categoryId, publish: false);

        // Read the list ANONYMOUSLY. This fixture authenticates as Admin, and an admin's list
        // includes drafts under its own cache key — so reading it as the admin would assert
        // nothing about the public entry this test is aiming at, and would pass either way.
        var before = await PublicListAsync();
        before!.Items.Should().NotContain(p => p.Sku == sku);

        (await Client.PostAsync($"{ProductsEndpoint}/{id}/publish", null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var after = await PublicListAsync();

        after!.Items.Should().Contain(p => p.Sku == sku,
            "publishing must evict the cached list, not leave it stale for the TTL");
    }

    /// <summary>
    /// The public (unauthenticated) product list, regardless of this fixture's admin bearer token.
    /// </summary>
    private async Task<PagedResponse<ProductResponse>?> PublicListAsync()
    {
        // A separate client, not a request with a nulled Authorization header: HttpClient merges
        // DefaultRequestHeaders into any request that does not already carry them, so clearing the
        // header on the message alone leaves this fixture's admin token in place and the "public"
        // read is silently still an admin read.
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(
            ProductsEndpoint + "?PageNumber=1&PageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
    }

    [Test]
    public async Task PublishEndpoints_RequireAuthentication()
    {
        var id = await CreateDraftViaApiAsync();
        Client.DefaultRequestHeaders.Authorization = null;

        (await Client.PostAsync($"{ProductsEndpoint}/{id}/publish", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await Client.PostAsync($"{ProductsEndpoint}/{id}/unpublish", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
