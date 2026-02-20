using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Integration tests for DELETE /api/v1/products/{id} endpoint (soft delete)
/// </summary>
[TestFixture]
[Category("Integration")]
public class DeleteProductTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public async Task DeleteProduct_WithExistingProduct_ShouldReturnNoContent()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Delete Product", CatalogDataHelper.GenerateUniqueSku("DEL"), 29.99m, 10, categoryId);

        // Act
        var response = await Client.DeleteAsync($"{ProductsEndpoint}/{productId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task DeleteProduct_SoftDeletedProduct_ShouldNotAppearInGetById()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Soft Delete Product", CatalogDataHelper.GenerateUniqueSku("SDEL"), 49.99m, 5, categoryId);

        // Act — delete the product
        var deleteResponse = await Client.DeleteAsync($"{ProductsEndpoint}/{productId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — product should not be found (query filter excludes deleted)
        var getResponse = await Client.GetAsync($"{ProductsEndpoint}/{productId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteProduct_WithNonExistentId_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await Client.DeleteAsync($"{ProductsEndpoint}/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteProduct_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.DeleteAsync($"{ProductsEndpoint}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
