using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Catalog.IntegrationTests.Security;

/// <summary>
/// Integration tests for security and authorization enforcement
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class SecurityTests : IntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";
    private const string CategoriesEndpoint = "/api/v1/categories";

    #region Product Write Endpoints — Require Admin Auth

    [Test]
    public async Task CreateProduct_WithoutAuth_ShouldReturnUnauthorized()
    {
        var request = new CreateProductRequest
        {
            Name = "Unauthorized",
            Sku = "UNAUTH-001",
            Price = 19.99m,
            StockQuantity = 10,
            CategoryId = Guid.NewGuid()
        };

        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateProduct_WithoutAuth_ShouldReturnUnauthorized()
    {
        var productId = Guid.NewGuid();
        var request = new UpdateProductRequest
        {
            ProductId = productId,
            Price = 39.99m,
            StockQuantity = 50
        };

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task DeleteProduct_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await Client.DeleteAsync($"{ProductsEndpoint}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Category Write Endpoints — Require Admin Auth

    [Test]
    public async Task CreateCategory_WithoutAuth_ShouldReturnUnauthorized()
    {
        var request = new CreateCategoryRequest { Name = "Unauthorized Category" };

        var response = await Client.PostAsJsonAsync(CategoriesEndpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateCategory_WithoutAuth_ShouldReturnUnauthorized()
    {
        var id = Guid.NewGuid();
        var request = new UpdateCategoryRequest { Id = id, Name = "Unauth", Description = "Desc" };

        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task DeleteCategory_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await Client.DeleteAsync($"{CategoriesEndpoint}/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Read Endpoints — Public Access

    [Test]
    public async Task GetProducts_WithoutAuth_ShouldReturnOk()
    {
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetCategories_WithoutAuth_ShouldReturnOk()
    {
        var response = await Client.GetAsync(CategoriesEndpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetProductById_WithoutAuth_ShouldNotReturnUnauthorized()
    {
        // GET by ID should be public — returns 404 for non-existent, not 401
        var response = await Client.GetAsync($"{ProductsEndpoint}/{Guid.NewGuid()}");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetCategoryById_WithoutAuth_ShouldNotReturnUnauthorized()
    {
        var response = await Client.GetAsync($"{CategoriesEndpoint}/{Guid.NewGuid()}");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Content Type Validation

    [Test]
    public async Task GetProducts_ShouldReturnJsonContentType()
    {
        var response = await Client.GetAsync($"{ProductsEndpoint}?PageNumber=1&PageSize=10");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Test]
    public async Task ProblemDetails_ShouldReturnProblemJsonContentType()
    {
        // Trigger a problem details response (non-existent product)
        var response = await Client.GetAsync($"{ProductsEndpoint}/{Guid.NewGuid()}");

        // The middleware returns application/problem+json
        var contentType = response.Content.Headers.ContentType?.MediaType;
        contentType.Should().BeOneOf("application/problem+json", "application/json");
    }

    #endregion
}
