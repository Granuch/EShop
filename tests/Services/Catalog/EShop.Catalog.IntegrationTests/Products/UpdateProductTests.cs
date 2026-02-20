using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Integration tests for PUT /api/v1/products/{id} endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class UpdateProductTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public async Task UpdateProduct_WithValidData_ShouldReturnNoContent()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Update Product", CatalogDataHelper.GenerateUniqueSku("UPD"), 29.99m, 100, categoryId);

        var request = new UpdateProductRequest
        {
            ProductId = productId,
            Price = 39.99m,
            StockQuantity = 50
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify update persisted
        var getResponse = await Client.GetAsync($"{ProductsEndpoint}/{productId}");
        var product = await getResponse.Content.ReadFromJsonAsync<ProductResponse>();
        product.Should().NotBeNull();
        product!.Price.Should().Be(39.99m);
        product.StockQuantity.Should().Be(50);
    }

    [Test]
    public async Task UpdateProduct_WithNonExistentId_ShouldReturnBadRequest()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var request = new UpdateProductRequest
        {
            ProductId = nonExistentId,
            Price = 39.99m,
            StockQuantity = 50
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{nonExistentId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Product.NotFound");
    }

    [Test]
    public async Task UpdateProduct_WithMismatchedRouteId_ShouldReturnBadRequest()
    {
        // Arrange
        var routeId = Guid.NewGuid();
        var bodyId = Guid.NewGuid();
        var request = new UpdateProductRequest
        {
            ProductId = bodyId,
            Price = 39.99m,
            StockQuantity = 50
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{routeId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Contain("IdMismatch");
    }

    [Test]
    public async Task UpdateProduct_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;
        var productId = Guid.NewGuid();
        var request = new UpdateProductRequest
        {
            ProductId = productId,
            Price = 39.99m,
            StockQuantity = 50
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateProduct_WithZeroPrice_ShouldReturnBadRequest()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Price Zero Product", CatalogDataHelper.GenerateUniqueSku("PZ"), 29.99m, 100, categoryId);

        var request = new UpdateProductRequest
        {
            ProductId = productId,
            Price = 0m,
            StockQuantity = 50
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
