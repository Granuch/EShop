using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Performance;

/// <summary>
/// Performance tests for Catalog API.
/// Validates response time, bulk operations, and pagination under load.
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Performance")]
public class PerformanceTests : IntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public async Task GetProducts_ShouldRespondWithin500ms()
    {
        // Arrange
        var sw = Stopwatch.StartNew();

        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=10");
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "product list query should respond within 500ms for integration tests");
    }

    [Test]
    public async Task BulkInsert_100Products_ShouldSucceedWithinReasonableTime()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var sw = Stopwatch.StartNew();

        // Act
        var ids = await CatalogDataHelper.CreateBulkProductsAsync(
            scope.ServiceProvider, 100, categoryId);
        sw.Stop();

        // Assert
        ids.Should().HaveCount(100);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            "bulk insert of 100 products should complete within 5 seconds");
    }

    [Test]
    public async Task Pagination_WithLargeDataset_ShouldReturnCorrectPageInfo()
    {
        // Arrange — ensure we have some products
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        await CatalogDataHelper.CreateBulkProductsAsync(scope.ServiceProvider, 25, categoryId);

        // Act — fetch page 1 with size 5
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.Items.Count().Should().BeLessThanOrEqualTo(5);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(5);
        result.TotalCount.Should().BeGreaterThan(5);
        result.TotalPages.Should().BeGreaterThan(1);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Test]
    public async Task Pagination_Page2_ShouldHavePreviousPage()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        await CatalogDataHelper.CreateBulkProductsAsync(scope.ServiceProvider, 15, categoryId);

        // Act — fetch page 2
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=2&PageSize=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        result.Should().NotBeNull();
        result!.PageNumber.Should().Be(2);
        result.HasPreviousPage.Should().BeTrue();
    }
}
