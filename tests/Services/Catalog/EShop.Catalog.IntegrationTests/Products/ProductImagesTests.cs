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

    [Test]
    public async Task AddAttribute_WithDuplicateName_ShouldReturnBadRequestAndNotAddIt()
    {
        // Arrange — the sub-resource path used to accept the same Name twice because the cap
        // and dedupe lived only in CreateProductCommandValidator, which never sees the rows
        // already persisted on the product.
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Dup Attr Product", CatalogDataHelper.GenerateUniqueSku("DUP"), 19.99m, 5, categoryId);

        var first = await Client.PostAsJsonAsync(
            $"{ProductsEndpoint}/{productId}/attributes", new AddProductAttributeRequest { Name = "Color", Value = "Red" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act — differs only by case, which the trimmed case-insensitive compare still catches
        var response = await Client.PostAsJsonAsync(
            $"{ProductsEndpoint}/{productId}/attributes", new AddProductAttributeRequest { Name = "  color  ", Value = "Blue" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{productId}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        detail!.Attributes.Should().ContainSingle("the rejected duplicate must not be persisted");
        detail.Attributes.Single().Value.Should().Be("Red");
    }

    [Test]
    public async Task AddAttribute_BeyondFiftyAttributes_ShouldReturnBadRequest()
    {
        // Arrange — 50 inline (the create validator's ceiling), then one more over the wire.
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var request = new CreateProductRequest
        {
            Name = "Capped Product",
            Sku = CatalogDataHelper.GenerateUniqueSku("CAP"),
            Price = 19.99m,
            StockQuantity = 5,
            CategoryId = categoryId,
            Attributes = Enumerable.Range(0, 50)
                .Select(i => new CreateProductAttributeRequestDto { Name = $"Attribute-{i}", Value = "value" })
                .ToList()
        };

        var createResponse = await Client.PostAsJsonAsync(ProductsEndpoint, request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        // Act
        var response = await Client.PostAsJsonAsync(
            $"{ProductsEndpoint}/{created!.Id}/attributes", new AddProductAttributeRequest { Name = "OneTooMany", Value = "value" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{created.Id}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        detail!.Attributes.Should().HaveCount(50);
    }

    [Test]
    public async Task AddAttribute_WhenRejected_ShouldExplainWhyInDetail()
    {
        // Arrange — every DomainException used to return the same opaque title with no detail,
        // so "duplicate name" and "cap exceeded" were byte-identical over the wire and the only
        // way to tell them apart was correlating TraceId against the server log.
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Detail Attr Product", CatalogDataHelper.GenerateUniqueSku("DTL"), 19.99m, 5, categoryId);

        await Client.PostAsJsonAsync(
            $"{ProductsEndpoint}/{productId}/attributes", new AddProductAttributeRequest { Name = "Color", Value = "Red" });

        // Act
        var response = await Client.PostAsJsonAsync(
            $"{ProductsEndpoint}/{productId}/attributes", new AddProductAttributeRequest { Name = "Color", Value = "Blue" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Detail.Should().NotBeNullOrWhiteSpace("the client must be able to tell which rule fired");
        problem.Detail.Should().Contain("Color").And.Contain("already exists");
    }

    [Test]
    public async Task CreateProduct_WithUnknownJsonField_ShouldReturnBadRequest()
    {
        // Arrange — an unmapped member used to be silently ignored, so a typo'd or stale field
        // name returned 201 and the value was quietly dropped.
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var json = $$"""
        {
            "name": "Unknown Field Product",
            "sku": "{{CatalogDataHelper.GenerateUniqueSku("UNK")}}",
            "price": 19.99,
            "stockQuantity": 5,
            "categoryId": "{{categoryId}}",
            "bogusField": "should be rejected"
        }
        """;
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await Client.PostAsync(ProductsEndpoint, content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an unmapped member must not be silently dropped");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.Detail.Should().Contain("bogusField", "the response must name the offending property");
        problem.Detail.Should().NotContain("EShop.Catalog.Application",
            "System.Text.Json's raw message embeds the target .NET type and must not be echoed to callers");
    }

    [Test]
    public async Task CreateProduct_WithKnownFieldsOnly_ShouldStillSucceed()
    {
        // Arrange — guards against Disallow being over-strict: the documented optional
        // collections, sent as explicit null, must still bind.
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var json = $$"""
        {
            "name": "Known Fields Product",
            "description": "fine",
            "sku": "{{CatalogDataHelper.GenerateUniqueSku("KNW")}}",
            "price": 19.99,
            "stockQuantity": 5,
            "categoryId": "{{categoryId}}",
            "images": null,
            "attributes": null
        }
        """;
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await Client.PostAsync(ProductsEndpoint, content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task CreateProduct_WithDescription_ShouldRoundTripItOnDetail()
    {
        // Arrange — Description was accepted and documented but silently dropped: Product.Create
        // took no such parameter, so the field always came back null.
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var request = new CreateProductRequest
        {
            Name = "Described Product",
            Description = "A useful description",
            Sku = CatalogDataHelper.GenerateUniqueSku("DSC"),
            Price = 19.99m,
            StockQuantity = 5,
            CategoryId = categoryId
        };

        // Act
        var createResponse = await Client.PostAsJsonAsync(ProductsEndpoint, request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        // Assert
        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{created!.Id}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        detail!.Description.Should().Be("A useful description");
    }

    [Test]
    public async Task CreateProduct_WithoutDescription_ShouldReturnNullDescription()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        var request = new CreateProductRequest
        {
            Name = "Undescribed Product",
            Sku = CatalogDataHelper.GenerateUniqueSku("NDS"),
            Price = 19.99m,
            StockQuantity = 5,
            CategoryId = categoryId
        };

        // Act
        var createResponse = await Client.PostAsJsonAsync(ProductsEndpoint, request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        // Assert
        var detail = await (await Client.GetAsync($"{ProductsEndpoint}/{created!.Id}"))
            .Content.ReadFromJsonAsync<ProductDetailsResponse>();
        detail!.Description.Should().BeNull();
    }
}
