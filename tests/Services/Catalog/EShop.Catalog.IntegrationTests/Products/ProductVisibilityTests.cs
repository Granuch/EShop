using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// D1 / H5a. <c>ProductStatus</c> gates public visibility.
///
/// <para>
/// Before Stage 4 this enum was an unfinished feature that read as a finished one:
/// <c>Product.Publish()</c> existed, had domain guards and was unit-tested, but had <b>no
/// production caller</b> — so every product was permanently <c>Draft</c> — and nothing filtered on
/// <c>Status</c>, so the public catalog served nothing but drafts while reporting
/// <c>"status": "Draft"</c> on each one. Both halves had to land together: publish/unpublish
/// commands without the read filter change nothing observable, and the filter without the commands
/// hides the entire catalog.
/// </para>
///
/// <para>
/// This fixture is <b>anonymous</b> — it extends <see cref="IntegrationTestBase"/>, not the
/// authenticated base — because the public view is the thing being asserted. Admin behaviour is
/// covered by <see cref="ProductVisibilityAdminTests"/>.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductVisibilityTests : IntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<(Guid Id, string Sku)> CreateDraftAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var sku = CatalogDataHelper.GenerateUniqueSku("DRAFT");

        var id = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Unpublished Product", sku, 42m, 5, categoryId, publish: false);

        return (id, sku);
    }

    [Test]
    public async Task AnUnpublishedProduct_IsAbsentFromThePublicList()
    {
        var (_, sku) = await CreateDraftAsync();

        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        page!.Items.Should().NotContain(p => p.Sku == sku,
            "a draft must not appear in the public catalog");
    }

    /// <summary>
    /// 404, not 403: the public catalog should not confirm that an unpublished product exists.
    /// </summary>
    [Test]
    public async Task AnUnpublishedProduct_Is404OnTheDetailEndpoint()
    {
        var (id, _) = await CreateDraftAsync();

        (await Client.GetAsync($"{ProductsEndpoint}/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The bypass this design has to withstand. <c>GetProductsQuery</c> is bound with
    /// <c>[AsParameters]</c>, so every public property is a query-string parameter — the endpoint
    /// overwrites <c>IncludeUnpublished</c> from the caller's role after binding, and without that
    /// single line any anonymous caller could read the unpublished catalog by asking for it.
    /// </summary>
    [Test]
    public async Task AnAnonymousCaller_CannotAskForUnpublishedProducts()
    {
        var (_, sku) = await CreateDraftAsync();

        var response = await Client.GetAsync(
            $"{ProductsEndpoint}?PageNumber=1&PageSize=100&IncludeUnpublished=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        page!.Items.Should().NotContain(p => p.Sku == sku,
            "the flag is set server-side from the caller's role and must ignore anything bound "
            + "from the query string");
    }

    [Test]
    public async Task ThePublicListReturnsOnlyActiveProducts()
    {
        await CreateDraftAsync();

        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=100");
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();

        page!.Items.Should().NotBeEmpty("the seeded catalog is published");
        page.Items.Should().OnlyContain(p => p.Status == ProductStatus.Active,
            "the whole public catalog used to be Draft, which is the defect this closes");
    }
}
