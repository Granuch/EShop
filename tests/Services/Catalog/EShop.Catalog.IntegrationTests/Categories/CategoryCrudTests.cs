using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Categories;

/// <summary>
/// Integration tests for Category API endpoints
/// </summary>
[TestFixture]
[Category("Integration")]
public class CategoryCrudTests : AuthenticatedIntegrationTestBase
{
    private const string CategoriesEndpoint = "/api/v1/categories";

    #region GET /api/v1/categories

    [Test]
    public async Task GetCategories_ShouldReturnOkWithCategories()
    {
        // Act
        var response = await Client.GetAsync(CategoriesEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();
        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterThanOrEqualTo(1); // seeded data
    }

    #endregion

    #region GET /api/v1/categories/{id}

    [Test]
    public async Task GetCategoryById_WithExistingId_ShouldReturnOk()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "GetById Category", $"getbyid-{Guid.NewGuid():N}");

        // Act
        var response = await Client.GetAsync($"{CategoriesEndpoint}/{categoryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(categoryId);
        result.Name.Should().Be("GetById Category");
    }

    [Test]
    public async Task GetCategoryById_WithNonExistentId_ShouldReturnNotFound()
    {
        // Act
        var response = await Client.GetAsync($"{CategoriesEndpoint}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/v1/categories/{id}/products

    [Test]
    public async Task GetProductsByCategory_WithExistingCategory_ShouldReturnProducts()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Products Category", $"prods-cat-{Guid.NewGuid():N}");

        await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Cat Product 1", CatalogDataHelper.GenerateUniqueSku("CP1"), 10m, 5, categoryId);
        await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Cat Product 2", CatalogDataHelper.GenerateUniqueSku("CP2"), 20m, 10, categoryId);

        // Act
        var response = await Client.GetAsync($"{CategoriesEndpoint}/{categoryId}/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<ProductResponse>>();
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result.Should().OnlyContain(p => p.CategoryId == categoryId);
    }

    #endregion

    #region POST /api/v1/categories

    [Test]
    public async Task CreateCategory_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        var request = new CreateCategoryRequest
        {
            Name = "New Category",
            Slug = $"new-cat-{Guid.NewGuid():N}"
        };

        // Act
        var response = await Client.PostAsJsonAsync(CategoriesEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var result = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        result.Should().NotBeNull();
        result!.Id.Should().NotBe(Guid.Empty);
    }

    [Test]
    public async Task CreateCategory_WithEmptyName_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateCategoryRequest { Name = "" };

        // Act
        var response = await Client.PostAsJsonAsync(CategoriesEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateCategory_WithParentId_ShouldReturnCreated()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var parentId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Parent Category", $"parent-{Guid.NewGuid():N}");

        var request = new CreateCategoryRequest
        {
            Name = "Child Category",
            Slug = $"child-{Guid.NewGuid():N}",
            ParentCategoryId = parentId
        };

        // Act
        var response = await Client.PostAsJsonAsync(CategoriesEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task CreateCategory_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;
        var request = new CreateCategoryRequest { Name = "Unauthorized Category" };

        // Act
        var response = await Client.PostAsJsonAsync(CategoriesEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region PUT /api/v1/categories/{id}

    [Test]
    public async Task UpdateCategory_WithValidData_ShouldReturnNoContent()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Update Category", $"upd-cat-{Guid.NewGuid():N}");

        var request = new UpdateCategoryRequest
        {
            Id = categoryId,
            Name = "Updated Category",
            Description = "Updated description"
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{categoryId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify update
        var getResponse = await Client.GetAsync($"{CategoriesEndpoint}/{categoryId}");
        var category = await getResponse.Content.ReadFromJsonAsync<CategoryResponse>();
        category!.Name.Should().Be("Updated Category");
    }

    [Test]
    public async Task UpdateCategory_WithNonExistentId_ShouldReturnBadRequest()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var request = new UpdateCategoryRequest
        {
            Id = nonExistentId,
            Name = "Updated",
            Description = "Desc"
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{nonExistentId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateCategory_WithMismatchedId_ShouldReturnBadRequest()
    {
        // Arrange
        var routeId = Guid.NewGuid();
        var request = new UpdateCategoryRequest
        {
            Id = Guid.NewGuid(), // different from route
            Name = "Mismatched",
            Description = "Desc"
        };

        // Act
        var response = await Client.PutAsJsonAsync($"{CategoriesEndpoint}/{routeId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region DELETE /api/v1/categories/{id}

    [Test]
    public async Task DeleteCategory_WithEmptyCategory_ShouldReturnNoContent()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Delete Category", $"del-cat-{Guid.NewGuid():N}");

        // Act
        var response = await Client.DeleteAsync($"{CategoriesEndpoint}/{categoryId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task DeleteCategory_WithProducts_ShouldReturnNotFound()
    {
        // Arrange — category with products should fail
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.CreateCategoryAsync(
            scope.ServiceProvider, "Has Products Category", $"hasp-{Guid.NewGuid():N}");

        await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Product In Category", CatalogDataHelper.GenerateUniqueSku("PIC"), 10m, 5, categoryId);

        // Act
        var response = await Client.DeleteAsync($"{CategoriesEndpoint}/{categoryId}");

        // Assert — returns 404 because error mapping uses StatusCodes.Status404NotFound for delete failures
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Category.HasProducts");
    }

    [Test]
    public async Task DeleteCategory_WithNonExistentId_ShouldReturnNotFound()
    {
        // Act
        var response = await Client.DeleteAsync($"{CategoriesEndpoint}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
