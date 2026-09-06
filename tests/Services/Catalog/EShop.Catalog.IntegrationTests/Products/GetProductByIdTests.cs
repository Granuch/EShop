using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Integration tests for GET /api/v1/products/{id} endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetProductByIdTests : IntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public async Task GetProductById_WithExistingProduct_ShouldReturnOk()
    {
        // Arrange — create a product via direct DB access
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Get By Id Product", CatalogDataHelper.GenerateUniqueSku("GID"), 49.99m, 10, categoryId);

        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}/{productId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ProductResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(productId);
        result.Name.Should().Be("Get By Id Product");
        result.Price.Should().Be(49.99m);
    }

    [Test]
    public async Task GetProductById_WithNonExistentId_ShouldReturnNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Product.NotFound");
    }

    [Test]
    public async Task GetProductById_WithInvalidGuid_ShouldReturnNotFound()
    {
        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}/not-a-guid");

        // Assert
        // Minimal API route constraint {id:guid} will not match and return 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetProductById_WithEmptyGuid_ShouldReturnBadRequest()
    {
        // Arrange — Guid.Empty satisfies the {id:guid} route constraint, so it reaches the
        // handler and is rejected by ValidationBehavior as a "Validation.Failed" Result. The
        // endpoint used to map every error to 404, reporting a malformed request as a missing
        // product; it now discriminates via ProblemForError.

        // Act
        var response = await Client.GetAsync($"{ProductsEndpoint}/{Guid.Empty}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Title.Should().Be("Validation.Failed");
    }
}
