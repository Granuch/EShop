using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Integration tests for product images and attributes: inline creation, the image
/// sub-resource endpoints, and the MainImageUrl that list and detail responses expose.
///
/// Caveat: these run on EF InMemory (see CatalogApiFactory), so the filtered unique index
/// guarding "at most one main image" at the database level is not exercised here — the
/// single-main assertions below verify the domain guard only.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProductImagesTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<(Guid ProductId, Guid CategoryId)> CreateProductWithImagesAsync(
        params CreateProductImageRequestDto[] images)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var request = new CreateProductRequest
        {
            Name = "Imaged Product",
            Sku = CatalogDataHelper.GenerateUniqueSku("IMG"),
            Price = 49.99m,
            StockQuantity = 10,
            CategoryId = categoryId,
            Images = images.ToList()
        };

        var response = await Client.PostAsJsonAsync(ProductsEndpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        return (created!.Id, categoryId);
    }

    [Test]
    public async Task CreateProduct_WithInlineImagesAndAttributes_ShouldReturnThemOnDetail()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var request = new CreateProductRequest
        {
            Name = "Inline Product",
            Sku = CatalogDataHelper.GenerateUniqueSku("INL"),
            Price = 59.99m,
            StockQuantity = 25,
            CategoryId = categoryId,
            Images =
            [
                new CreateProductImageRequestDto { Url = "https://cdn.example.com/a.jpg", AltText = "A", DisplayOrder = 0 },
                new CreateProductImageRequestDto { Url = "https://cdn.example.com/b", AltText = "B", DisplayOrder = 1 }
            ],
            Attributes =
            [
                new CreateProductAttributeRequestDto { Name = "Color", Value = "Red" },
                new CreateProductAttributeRequestDto { Name = "Size", Value = "Large" }
            ]
        };

        // Act
        var createResponse = await Client.PostAsJsonAsync(ProductsEndpoint, request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        var detailResponse = await Client.GetAsync($"{ProductsEndpoint}/{created!.Id}");

        // Assert
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<ProductDetailsResponse>();

        detail.Should().NotBeNull();
        detail!.Images.Should().HaveCount(2);
        detail.Attributes.Should().HaveCount(2);

        // Gallery order is DisplayOrder, then CreatedAt
        detail.Images.Select(i => i.Url).Should()
            .ContainInOrder("https://cdn.example.com/a.jpg", "https://cdn.example.com/b");

        // The extensionless CDN URL is accepted — there is no file-extension allowlist
        detail.Images.Should().Contain(i => i.Url == "https://cdn.example.com/b");

        detail.Images.Count(i => i.IsMain).Should().Be(1);
        detail.MainImageUrl.Should().Be("https://cdn.example.com/a.jpg");
        detail.Attributes.Select(a => a.Name).Should().BeEquivalentTo("Color", "Size");
    }

    [Test]
    public async Task GetProducts_WithProductThatHasImages_ShouldReturnNonNullMainImageUrl()
    {
        // Arrange
        var (productId, categoryId) = await CreateProductWithImagesAsync(
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/list-main.jpg", DisplayOrder = 0 });

        // Act — filter by category so the product is on the first page regardless of data volume
        var response = await Client.GetAsync($"{ProductsEndpoint}?categoryId={categoryId}&pageSize=100");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();

        var listed = page!.Items.Single(p => p.Id == productId);
        listed.MainImageUrl.Should().Be("https://cdn.example.com/list-main.jpg");
    }

    [Test]
    public async Task AddImage_ToExistingProduct_ShouldReturnCreatedAndAppearOnDetail()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Add Image Product", CatalogDataHelper.GenerateUniqueSku("ADD"), 19.99m, 5, categoryId);

        var request = new AddProductImageRequest
        {
            Url = "https://cdn.example.com/added.jpg",
            AltText = "Added",
            DisplayOrder = 0
        };

        // Act
        var response = await Client.PostAsJsonAsync($"{ProductsEndpoint}/{productId}/images", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        created!.Id.Should().NotBe(Guid.Empty);

        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();

        detail!.Images.Should().ContainSingle();
        detail.Images.Single().Id.Should().Be(created.Id);
        detail.Images.Single().IsMain.Should().BeTrue();
    }

    [Test]
    public async Task AddImage_ToNonExistentProduct_ShouldReturnNotFound()
    {
        // Arrange
        var request = new AddProductImageRequest { Url = "https://cdn.example.com/x.jpg", DisplayOrder = 0 };

        // Act
        var response = await Client.PostAsJsonAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/images", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Title.Should().Be("Product.NotFound");
    }

    [Test]
    public async Task AddImage_WithMalformedUrl_ShouldReturnBadRequest()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Bad Url Product", CatalogDataHelper.GenerateUniqueSku("BAD"), 19.99m, 5, categoryId);

        var request = new AddProductImageRequest { Url = "not-an-absolute-url", DisplayOrder = 0 };

        // Act
        var response = await Client.PostAsJsonAsync($"{ProductsEndpoint}/{productId}/images", request);

        // Assert — over-length and malformed URLs are a 400, never a 500
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SetMainImage_ShouldChangeWhichUrlTheListReturns()
    {
        // Arrange
        var (productId, categoryId) = await CreateProductWithImagesAsync(
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/first.jpg", DisplayOrder = 0 },
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/second.jpg", DisplayOrder = 1 });

        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        var secondImageId = detail!.Images.Single(i => i.Url.EndsWith("second.jpg")).Id;

        // Act
        var setMainResponse = await Client.PutAsync($"{ProductsEndpoint}/{productId}/images/{secondImageId}/main", null);

        // Assert
        setMainResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updatedDetail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        updatedDetail!.MainImageUrl.Should().Be("https://cdn.example.com/second.jpg");
        updatedDetail.Images.Count(i => i.IsMain).Should().Be(1);

        var page = await (await Client.GetAsync($"{ProductsEndpoint}?categoryId={categoryId}&pageSize=100"))
            .Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>();
        page!.Items.Single(p => p.Id == productId).MainImageUrl
            .Should().Be("https://cdn.example.com/second.jpg",
                "the list projection picks IsMain first, not the lowest DisplayOrder");
    }

    [Test]
    public async Task SetMainImage_WithNonExistentImage_ShouldReturnNotFound()
    {
        // Arrange
        var (productId, _) = await CreateProductWithImagesAsync(
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/only.jpg", DisplayOrder = 0 });

        // Act
        var response = await Client.PutAsync($"{ProductsEndpoint}/{productId}/images/{Guid.NewGuid()}/main", null);

        // Assert — a missing image is a 404, not the 400 a raw DomainException would produce
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Title.Should().Be("ProductImage.NotFound");
    }

    [Test]
    public async Task RemoveMainImage_ShouldPromoteSuccessor()
    {
        // Arrange
        var (productId, _) = await CreateProductWithImagesAsync(
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/main.jpg", DisplayOrder = 0 },
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/next.jpg", DisplayOrder = 1 });

        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        var mainImageId = detail!.Images.Single(i => i.IsMain).Id;

        // Act
        var deleteResponse = await Client.DeleteAsync($"{ProductsEndpoint}/{productId}/images/{mainImageId}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updatedDetail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        updatedDetail!.Images.Should().ContainSingle();
        updatedDetail.Images.Single().Url.Should().Be("https://cdn.example.com/next.jpg");
        updatedDetail.Images.Single().IsMain.Should().BeTrue("removing the main image promotes its successor");
        updatedDetail.MainImageUrl.Should().Be("https://cdn.example.com/next.jpg");
    }

    [Test]
    public async Task RemoveLastImage_ShouldLeaveProductWithNoMainImage()
    {
        // Arrange
        var (productId, _) = await CreateProductWithImagesAsync(
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/only.jpg", DisplayOrder = 0 });

        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        var onlyImageId = detail!.Images.Single().Id;

        // Act
        var deleteResponse = await Client.DeleteAsync($"{ProductsEndpoint}/{productId}/images/{onlyImageId}");

        // Assert — zero mains is legitimate; it is exactly what the filtered unique index permits
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updatedDetail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        updatedDetail!.Images.Should().BeEmpty();
        updatedDetail.MainImageUrl.Should().BeNull();
    }

    [Test]
    public async Task RemoveImage_WithNonExistentImage_ShouldReturnNotFound()
    {
        // Arrange
        var (productId, _) = await CreateProductWithImagesAsync(
            new CreateProductImageRequestDto { Url = "https://cdn.example.com/only.jpg", DisplayOrder = 0 });

        // Act
        var response = await Client.DeleteAsync($"{ProductsEndpoint}/{productId}/images/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Title.Should().Be("ProductImage.NotFound");
    }

    [Test]
    public async Task AddAttribute_ToExistingProduct_ShouldReturnCreatedAndAppearOnDetail()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Attr Product", CatalogDataHelper.GenerateUniqueSku("ATR"), 19.99m, 5, categoryId);

        var request = new AddProductAttributeRequest { Name = "Material", Value = "Cotton" };

        // Act
        var response = await Client.PostAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();

        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();

        detail!.Attributes.Should().ContainSingle();
        detail.Attributes.Single().Id.Should().Be(created!.Id);
        detail.Attributes.Single().Name.Should().Be("Material");
        detail.Attributes.Single().Value.Should().Be("Cotton");
    }

    [Test]
    public async Task AddAttribute_ToNonExistentProduct_ShouldReturnNotFound()
    {
        // Arrange
        var request = new AddProductAttributeRequest { Name = "Material", Value = "Cotton" };

        // Act
        var response = await Client.PostAsJsonAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/attributes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Title.Should().Be("Product.NotFound");
    }
}
