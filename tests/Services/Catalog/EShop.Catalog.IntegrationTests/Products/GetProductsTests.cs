using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Integration tests for GET /api/v1/products endpoints.
/// Note: PageNumber and PageSize must always be provided in the query string
/// because [AsParameters] binding does not use record init defaults.
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetProductsTests : IntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public async Task GetProducts_ShouldReturnOkWithPagedResult()
    {
        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeNull();
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Test]
    public async Task GetProducts_WithPagination_ShouldRespectPageSize()
    {
        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.Items.Count().Should().BeLessThanOrEqualTo(2);
        result.PageSize.Should().Be(2);
    }

    [Test]
    public async Task GetProducts_WithHighPageNumber_ShouldReturnEmptyItems()
    {
        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=9999&PageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
    }

    [Test]
    public async Task GetProducts_WithPriceFilter_ShouldFilterByPriceRange()
    {
        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=50&MinPrice=10&MaxPrice=30");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.Items.Should().OnlyContain(p => p.Price >= 10 && p.Price <= 30);
    }

    [Test]
    public async Task GetProducts_WithCategoryFilter_ShouldFilterByCategory()
    {
        // Arrange — get a category ID from seeded data
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=50&CategoryId={categoryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.Items.Should().OnlyContain(p => p.CategoryId == categoryId);
    }

    [Test]
    public async Task GetProducts_WithSortByPrice_ShouldReturnSortedResults()
    {
        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=50&SortBy=1&IsDescending=false");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();

        var prices = result!.Items.Select(p => p.Price).ToList();
        prices.Should().BeInAscendingOrder();
    }

    [Test]
    public async Task GetProducts_WithSortByPriceDescending_ShouldReturnDescendingOrder()
    {
        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=50&SortBy=1&IsDescending=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();

        var prices = result!.Items.Select(p => p.Price).ToList();
        prices.Should().BeInDescendingOrder();
    }

    [Test]
    public async Task GetProducts_WithDefaultPagination_ShouldReturnOk()
    {
        // Act — omit all parameters; defaults apply (PageNumber=1, PageSize=10)
        var response = await Client.GetAsync(ProductsEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(10);
    }
}
