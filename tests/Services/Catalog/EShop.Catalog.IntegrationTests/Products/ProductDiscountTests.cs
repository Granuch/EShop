using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// H5b / D3. <c>DiscountPrice</c> is settable, and the value reaches the read paths another
/// bounded context prices against.
///
/// <para>
/// Before Stage 5 the field had no mutator anywhere — no domain method, no command, no validator —
/// so it was permanently <c>null</c>, while still being projected into both DTOs and consumed by
/// Basket's <c>CatalogProductCatalogReader</c> as <c>payload.DiscountPrice ?? payload.Price</c>: a
/// pricing branch in another service that Catalog could not reach. Decided 2026-09-08 (D3) that the
/// field stays in Catalog and becomes real rather than being deleted along with Basket's coalesce.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductDiscountTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<Guid> CreatePublishedProductAsync(decimal price)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        return await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider,
            "Discountable Product",
            CatalogDataHelper.GenerateUniqueSku("DISC"),
            price,
            10,
            categoryId);
    }

    private async Task<ProductDetailsResponse> GetProductAsync(Guid id)
    {
        var response = await Client.GetAsync($"{ProductsEndpoint}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ProductDetailsResponse>())!;
    }

    private Task<HttpResponseMessage> SetDiscountAsync(Guid id, decimal discountPrice)
        => Client.PutAsJsonAsync(
            $"{ProductsEndpoint}/{id}/discount",
            new SetProductDiscountRequest { DiscountPrice = discountPrice });

    [Test]
    public async Task SettingADiscount_IsVisibleOnTheProductDetail()
    {
        var id = await CreatePublishedProductAsync(100m);

        (await SetDiscountAsync(id, 79.99m)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var product = await GetProductAsync(id);
        product.Price.Should().Be(100m, "the list price is unchanged — a discount is a second price");
        product.DiscountPrice.Should().Be(79.99m);
    }

    /// <summary>
    /// The list projection is a separate <c>Select</c> from the detail mapping, so it can drift.
    /// Basket reads the detail endpoint, but a storefront prices from the list.
    /// </summary>
    [Test]
    public async Task SettingADiscount_IsVisibleInTheProductList()
    {
        var id = await CreatePublishedProductAsync(50m);
        await SetDiscountAsync(id, 25m);

        var page = await (await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=100"))
            .Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();

        page!.Items.Should().ContainSingle(p => p.Id == id)
            .Which.DiscountPrice.Should().Be(25m);
    }

    [Test]
    public async Task ClearingADiscount_RemovesIt()
    {
        var id = await CreatePublishedProductAsync(100m);
        await SetDiscountAsync(id, 60m);

        (await Client.DeleteAsync($"{ProductsEndpoint}/{id}/discount")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await GetProductAsync(id)).DiscountPrice.Should().BeNull();
    }

    /// <summary>
    /// Idempotent, matching publish/unpublish: a retried DELETE after a dropped response must not
    /// report an error for an operation that already succeeded.
    /// </summary>
    [Test]
    public async Task ClearingAnAbsentDiscount_IsANoOpRatherThanAnError()
    {
        var id = await CreatePublishedProductAsync(100m);

        (await Client.DeleteAsync($"{ProductsEndpoint}/{id}/discount")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"{ProductsEndpoint}/{id}/discount")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The invariant needs the persisted product, so it cannot live in the validator. It is a
    /// domain guard, and a <c>DomainException</c> carries its own message into <c>detail</c>.
    /// </summary>
    [Test]
    public async Task ADiscountAtOrAboveTheListPrice_IsRejected()
    {
        var id = await CreatePublishedProductAsync(100m);

        (await SetDiscountAsync(id, 100m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SetDiscountAsync(id, 150m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await GetProductAsync(id)).DiscountPrice.Should().BeNull("a rejected request must persist nothing");
    }

    /// <summary>
    /// This one is caught by the validator rather than the domain, because it needs nothing but the
    /// request — so it surfaces as a Validation.Failed Result, which the endpoint maps to 400.
    /// </summary>
    [Test]
    public async Task ANonPositiveDiscount_IsRejected()
    {
        var id = await CreatePublishedProductAsync(100m);

        (await SetDiscountAsync(id, 0m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SetDiscountAsync(id, -5m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SettingADiscountOnAMissingProduct_Is404()
    {
        (await SetDiscountAsync(Guid.NewGuid(), 10m)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.DeleteAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/discount")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Lowering the list price past an active discount is refused rather than silently ending the
    /// promotion. Leaving it would make the effective price exceed the list price — Basket charging
    /// more than the catalog displays — and clearing it automatically is the "PUT wipes a field the
    /// caller never mentioned" shape the repo already carries as BUG-09.
    /// </summary>
    [Test]
    public async Task LoweringThePriceBelowAnActiveDiscount_IsRejected()
    {
        var id = await CreatePublishedProductAsync(100m);
        await SetDiscountAsync(id, 80m);

        var response = await Client.PutAsJsonAsync(
            $"{ProductsEndpoint}/{id}",
            new UpdateProductRequest { ProductId = id, Price = 70m, StockQuantity = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var product = await GetProductAsync(id);
        product.Price.Should().Be(100m);
        product.DiscountPrice.Should().Be(80m);
    }

    [Test]
    public async Task LoweringThePriceAfterClearingTheDiscount_Succeeds()
    {
        var id = await CreatePublishedProductAsync(100m);
        await SetDiscountAsync(id, 80m);

        await Client.DeleteAsync($"{ProductsEndpoint}/{id}/discount");

        var response = await Client.PutAsJsonAsync(
            $"{ProductsEndpoint}/{id}",
            new UpdateProductRequest { ProductId = id, Price = 70m, StockQuantity = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetProductAsync(id)).Price.Should().Be(70m);
    }

    /// <summary>
    /// A discount changes what a customer pays, so it must evict the same entries a price change
    /// does. Without it the pre-discount detail response would be served for its full 5-minute TTL —
    /// and Basket reads that endpoint to price an item being added.
    /// </summary>
    [Test]
    public async Task SettingADiscount_InvalidatesTheCachedProductDetail()
    {
        var id = await CreatePublishedProductAsync(100m);

        (await GetProductAsync(id)).DiscountPrice.Should().BeNull();

        await SetDiscountAsync(id, 65m);

        (await GetProductAsync(id)).DiscountPrice.Should().Be(65m,
            "the cached detail must be evicted, not left stale for the TTL");
    }

    [Test]
    public async Task DiscountEndpoints_RequireAuthentication()
    {
        var id = await CreatePublishedProductAsync(100m);

        using var anonymous = Factory.CreateClient();

        (await anonymous.PutAsJsonAsync(
            $"{ProductsEndpoint}/{id}/discount",
            new SetProductDiscountRequest { DiscountPrice = 50m })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        (await anonymous.DeleteAsync($"{ProductsEndpoint}/{id}/discount")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<PagedResponse<ProductResponse>> ListAsync(string query)
    {
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=100{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>())!;
    }

    /// <summary>
    /// Catalog audit Stage 10. Price filters compare the <b>effective</b> price, the one Basket
    /// charges. On the list price, a product discounted from 100 to 79.99 was hidden from a
    /// MaxPrice=80 shopper and shown to a MinPrice=90 one who could not buy it at that price.
    /// </summary>
    [Test]
    public async Task PriceFilters_CompareTheDiscountedPrice()
    {
        var id = await CreatePublishedProductAsync(100m);
        await SetDiscountAsync(id, 79.99m);

        (await ListAsync("&MinPrice=79&MaxPrice=80")).Items.Should().Contain(p => p.Id == id,
            "the customer pays 79.99, so a shopper capped at 80 must see it");
        (await ListAsync("&MinPrice=90")).Items.Should().NotContain(p => p.Id == id,
            "its 100 list price is not what anyone pays while the discount is active");
    }

    /// <summary>
    /// The Price sort orders by the same effective price. The discounted product is created last so
    /// the discount's family bump is the final write before the read; rows seeded through the
    /// DbContext bypass cache invalidation.
    /// </summary>
    [Test]
    public async Task ThePriceSort_OrdersByTheDiscountedPrice()
    {
        var plain = await CreatePublishedProductAsync(50m);
        var discounted = await CreatePublishedProductAsync(100m);
        await SetDiscountAsync(discounted, 10m);

        var items = (await ListAsync("&MinPrice=5&MaxPrice=60&SortBy=1&IsDescending=false")).Items.ToList();

        items.Select(p => p.DiscountPrice ?? p.Price).Should().BeInAscendingOrder();
        items.FindIndex(p => p.Id == discounted).Should().BeLessThan(items.FindIndex(p => p.Id == plain),
            "10 effective sorts before 50, even though its list price is 100");
    }
}
