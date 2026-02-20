using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Integration tests for POST /api/v1/products endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class CreateProductTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public async Task CreateProduct_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var request = new CreateProductRequest
        {
            Name = "New Test Product",
            Description = "A test product",
            Sku = CatalogDataHelper.GenerateUniqueSku("NEW"),
            Price = 59.99m,
            StockQuantity = 25,
            CategoryId = categoryId
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/products/");

        var result = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBe(Guid.Empty);
    }

    [Test]
    public async Task CreateProduct_WithDuplicateSku_ShouldReturnBadRequest()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var sku = CatalogDataHelper.GenerateUniqueSku("DUP");

        await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Existing Product", sku, 29.99m, 10, categoryId);

        var request = new CreateProductRequest
        {
            Name = "Duplicate SKU Product",
            Sku = sku,
            Price = 39.99m,
            StockQuantity = 5,
            CategoryId = categoryId
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Product.SkuConflict");
    }

    [Test]
    public async Task CreateProduct_WithNonExistentCategory_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateProductRequest
        {
            Name = "Orphan Product",
            Sku = CatalogDataHelper.GenerateUniqueSku("ORPH"),
            Price = 19.99m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid() // non-existent
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Category.NotFound");
    }

    [Test]
    public async Task CreateProduct_WithEmptyName_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateProductRequest
        {
            Name = "",
            Sku = "EMPTY-NAME",
            Price = 19.99m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateProduct_WithZeroPrice_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateProductRequest
        {
            Name = "Zero Price Product",
            Sku = "ZERO-PRICE",
            Price = 0m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateProduct_WithNegativePrice_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateProductRequest
        {
            Name = "Negative Price Product",
            Sku = "NEG-PRICE",
            Price = -10m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateProduct_WithNegativeStock_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateProductRequest
        {
            Name = "Negative Stock Product",
            Sku = "NEG-STOCK",
            Price = 19.99m,
            StockQuantity = -1,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateProduct_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        var request = new CreateProductRequest
        {
            Name = "Unauthorized Product",
            Sku = "UNAUTH-001",
            Price = 19.99m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid()
        };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
