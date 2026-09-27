using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// Admin panel S3 — the child endpoints a product's images and attributes gained: editing an image,
/// reordering the gallery, and editing, deleting or replacing attributes. Before this stage images
/// could only be added, removed or promoted, and attributes could only be added.
/// </summary>
/// <remarks>
/// Several of these exercise the shape the root guide calls the <c>ValueGeneratedNever</c>
/// regression: adding a child to an <b>already-persisted</b> aggregate. Without that mapping EF
/// treats the domain-assigned Guid as an existing row and issues an UPDATE that matches nothing,
/// producing a 409. <c>ReplaceProductAttributes</c> is the new instance of it.
/// </remarks>
[TestFixture]
[Category("Integration")]
public class ProductChildEditingTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<Guid> SeedProductAsync(string name = "Child editing")
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        return await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, name, CatalogDataHelper.GenerateUniqueSku("S3"), 20m, 10, categoryId);
    }

    private async Task<Guid> AddImageAsync(Guid productId, string url, string? altText = null, int displayOrder = 0)
    {
        var response = await Client.PostAsJsonAsync($"{ProductsEndpoint}/{productId}/images",
            new AddProductImageRequest { Url = url, AltText = altText, DisplayOrder = displayOrder });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private async Task<Guid> AddAttributeAsync(Guid productId, string name, string value)
    {
        var response = await Client.PostAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new AddProductAttributeRequest { Name = name, Value = value });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private async Task<ProductDetailsResponse> ReadAsync(Guid productId)
    {
        var response = await Client.GetAsync($"{ProductsEndpoint}/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ProductDetailsResponse>())!;
    }

    #region Images

    [Test]
    public async Task UpdateImage_ReplacesUrlAndAltText()
    {
        var productId = await SeedProductAsync();
        var imageId = await AddImageAsync(productId, "https://cdn.example.com/before.jpg", "Before");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/{imageId}",
            new UpdateProductImageRequest { Url = "https://cdn.example.com/after.jpg", AltText = "After" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var image = (await ReadAsync(productId)).Images.Single();
        image.Url.Should().Be("https://cdn.example.com/after.jpg");
        image.AltText.Should().Be("After");
    }

    [Test]
    public async Task UpdateImage_WithoutAltText_ClearsIt()
    {
        var productId = await SeedProductAsync();
        var imageId = await AddImageAsync(productId, "https://cdn.example.com/a.jpg", "Had alt text");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/{imageId}",
            new UpdateProductImageRequest { Url = "https://cdn.example.com/a.jpg" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync(productId)).Images.Single().AltText.Should().BeNull();
    }

    [Test]
    public async Task UpdateImage_WithAnUnknownImageId_Is404()
    {
        var productId = await SeedProductAsync();
        await AddImageAsync(productId, "https://cdn.example.com/a.jpg");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/{Guid.NewGuid()}",
            new UpdateProductImageRequest { Url = "https://cdn.example.com/b.jpg" });

        // 404, not the 400 a DomainException would give — the handler pre-checks for exactly this.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("ProductImage.NotFound");
    }

    [Test]
    public async Task UpdateImage_OnAnUnknownProduct_Is404()
    {
        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/images/{Guid.NewGuid()}",
            new UpdateProductImageRequest { Url = "https://cdn.example.com/b.jpg" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Product.NotFound");
    }

    [Test]
    public async Task UpdateImage_ToAUrlAnotherImageHolds_IsRefused_AndChangesNothing()
    {
        var productId = await SeedProductAsync();
        var first = await AddImageAsync(productId, "https://cdn.example.com/a.jpg");
        await AddImageAsync(productId, "https://cdn.example.com/b.jpg", displayOrder: 1);

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/{first}",
            new UpdateProductImageRequest { Url = "https://cdn.example.com/b.jpg", AltText = "Should not stick" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // TransactionBehavior commits on any non-exception return, so assert the stored state too.
        var images = (await ReadAsync(productId)).Images;
        images.Single(i => i.Id == first).Url.Should().Be("https://cdn.example.com/a.jpg");
        images.Single(i => i.Id == first).AltText.Should().BeNull();
    }

    [Test]
    public async Task UpdateImage_ResendingItsOwnUrl_IsAllowed()
    {
        var productId = await SeedProductAsync();
        var imageId = await AddImageAsync(productId, "https://cdn.example.com/a.jpg", "Old");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/{imageId}",
            new UpdateProductImageRequest { Url = "https://cdn.example.com/a.jpg", AltText = "New" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync(productId)).Images.Single().AltText.Should().Be("New");
    }

    [Test]
    public async Task UpdateImage_WithANonHttpUrl_Is400()
    {
        var productId = await SeedProductAsync();
        var imageId = await AddImageAsync(productId, "https://cdn.example.com/a.jpg");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/{imageId}",
            new UpdateProductImageRequest { Url = "ftp://cdn.example.com/a.jpg" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ReorderImages_RewritesDisplayOrder_AndKeepsTheMainImage()
    {
        var productId = await SeedProductAsync();
        var a = await AddImageAsync(productId, "https://cdn.example.com/a.jpg", displayOrder: 0);
        var b = await AddImageAsync(productId, "https://cdn.example.com/b.jpg", displayOrder: 1);
        var c = await AddImageAsync(productId, "https://cdn.example.com/c.jpg", displayOrder: 2);

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/reorder",
            new ReorderProductImagesRequest { ImageIds = [c, a, b] });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stored = await db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == productId)
            .ToDictionaryAsync(i => i.Id, i => i);

        stored[c].DisplayOrder.Should().Be(0);
        stored[a].DisplayOrder.Should().Be(1);
        stored[b].DisplayOrder.Should().Be(2);
        stored.Values.Single(i => i.IsMain).Id.Should().Be(a, "reordering must not change which image is main");
    }

    [Test]
    public async Task ReorderImages_WithAPartialList_Is400_AndChangesNothing()
    {
        var productId = await SeedProductAsync();
        var a = await AddImageAsync(productId, "https://cdn.example.com/a.jpg", displayOrder: 0);
        var b = await AddImageAsync(productId, "https://cdn.example.com/b.jpg", displayOrder: 5);

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/reorder",
            new ReorderProductImagesRequest { ImageIds = [b] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stored = await db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == productId)
            .ToDictionaryAsync(i => i.Id, i => i.DisplayOrder);

        stored[a].Should().Be(0);
        stored[b].Should().Be(5, "a rejected reorder must leave every position untouched");
    }

    [Test]
    public async Task ReorderImages_WithAnUnknownId_Is404()
    {
        var productId = await SeedProductAsync();
        var a = await AddImageAsync(productId, "https://cdn.example.com/a.jpg");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/reorder",
            new ReorderProductImagesRequest { ImageIds = [a, Guid.NewGuid()] });

        // An id that is not this product's is a missing resource (404); a malformed list — wrong
        // count, duplicates — is a bad request (400). The handler splits them for that reason.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("ProductImage.NotFound");
    }

    [Test]
    public async Task ReorderImages_WithADuplicateId_Is400()
    {
        var productId = await SeedProductAsync();
        var a = await AddImageAsync(productId, "https://cdn.example.com/a.jpg");
        await AddImageAsync(productId, "https://cdn.example.com/b.jpg", displayOrder: 1);

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/reorder",
            new ReorderProductImagesRequest { ImageIds = [a, a] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ReorderImages_WithNoList_Is400()
    {
        // System.Text.Json writes an omitted array as explicit null, which is why ImageIds is
        // nullable and the validator checks NotNull rather than the handler dereferencing it.
        var productId = await SeedProductAsync();
        await AddImageAsync(productId, "https://cdn.example.com/a.jpg");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/images/reorder",
            new ReorderProductImagesRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Attributes

    [Test]
    public async Task UpdateAttribute_ReplacesNameAndValue_AndKeepsItsId()
    {
        var productId = await SeedProductAsync();
        var attributeId = await AddAttributeAsync(productId, "Colour", "Red");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes/{attributeId}",
            new UpdateProductAttributeRequest { Name = "Color", Value = "Blue" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var attribute = (await ReadAsync(productId)).Attributes.Single();
        attribute.Id.Should().Be(attributeId);
        attribute.Name.Should().Be("Color");
        attribute.Value.Should().Be("Blue");
    }

    [Test]
    public async Task UpdateAttribute_WithAnUnknownId_Is404()
    {
        var productId = await SeedProductAsync();
        await AddAttributeAsync(productId, "Color", "Red");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes/{Guid.NewGuid()}",
            new UpdateProductAttributeRequest { Name = "Size", Value = "L" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("ProductAttribute.NotFound");
    }

    [Test]
    public async Task UpdateAttribute_ToANameASiblingHolds_Is400_AndChangesNothing()
    {
        var productId = await SeedProductAsync();
        var size = await AddAttributeAsync(productId, "Size", "L");
        await AddAttributeAsync(productId, "Color", "Red");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes/{size}",
            new UpdateProductAttributeRequest { Name = "COLOR", Value = "Blue" });

        // 400 from the aggregate, which can see the sibling. The 409 path is the one it cannot see
        // — see ProductAttributeConflictTests.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var attributes = (await ReadAsync(productId)).Attributes;
        attributes.Single(a => a.Id == size).Name.Should().Be("Size");
        attributes.Single(a => a.Id == size).Value.Should().Be("L");
    }

    [Test]
    public async Task RemoveAttribute_RemovesIt()
    {
        var productId = await SeedProductAsync();
        var colorId = await AddAttributeAsync(productId, "Color", "Red");
        await AddAttributeAsync(productId, "Size", "L");

        var response = await Client.DeleteAsync($"{ProductsEndpoint}/{productId}/attributes/{colorId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync(productId)).Attributes.Select(a => a.Name).Should().BeEquivalentTo(["Size"]);
    }

    [Test]
    public async Task RemoveAttribute_WithAnUnknownId_Is404()
    {
        var productId = await SeedProductAsync();

        var response = await Client.DeleteAsync($"{ProductsEndpoint}/{productId}/attributes/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("ProductAttribute.NotFound");
    }

    [Test]
    public async Task ReplaceAttributes_AddsRemovesAndUpdates_OnAPersistedProduct()
    {
        // The ValueGeneratedNever shape: "Material" is a brand-new child added to an aggregate that
        // is already in the database. Without that mapping EF would issue an UPDATE for its
        // domain-assigned Guid, match nothing, and this would come back 409.
        var productId = await SeedProductAsync();
        var colorId = await AddAttributeAsync(productId, "Color", "Red");
        await AddAttributeAsync(productId, "Size", "L");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new ReplaceProductAttributesRequest
            {
                Attributes =
                [
                    new ReplaceProductAttributeItem { Name = "Color", Value = "Blue" },
                    new ReplaceProductAttributeItem { Name = "Material", Value = "Cotton" }
                ]
            });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());

        var attributes = (await ReadAsync(productId)).Attributes;
        attributes.Select(a => (a.Name, a.Value)).Should().BeEquivalentTo(
            [("Color", "Blue"), ("Material", "Cotton")]);
        attributes.Single(a => a.Name == "Color").Id.Should().Be(colorId,
            "a retained attribute keeps its id — the handler reconciles rather than clearing and re-adding");
    }

    [Test]
    public async Task ReplaceAttributes_ChangingOnlyAValue_DoesNotCollideWithItself()
    {
        // The reason reconciliation matters against M1's index. Clearing and re-adding would put a
        // DELETE and an INSERT of the same (ProductId, lower(Name)) key in one SaveChanges, and EF
        // gives no ordering guarantee — so this save, which changes nothing but a value, could fail
        // with 23505.
        var productId = await SeedProductAsync();
        await AddAttributeAsync(productId, "Color", "Red");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new ReplaceProductAttributesRequest
            {
                Attributes = [new ReplaceProductAttributeItem { Name = "Color", Value = "Blue" }]
            });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await ReadAsync(productId)).Attributes.Single().Value.Should().Be("Blue");
    }

    [Test]
    public async Task ReplaceAttributes_WithAnEmptyList_ClearsThemAll()
    {
        var productId = await SeedProductAsync();
        await AddAttributeAsync(productId, "Color", "Red");
        await AddAttributeAsync(productId, "Size", "L");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new ReplaceProductAttributesRequest { Attributes = [] });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync(productId)).Attributes.Should().BeEmpty();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await db.ProductAttributes.CountAsync(a => a.ProductId == productId)).Should().Be(0,
            "the rows must actually be deleted, not merely absent from the projection");
    }

    [Test]
    public async Task ReplaceAttributes_WithNoList_Is400()
    {
        // Absent is not the same as empty: an empty array clears the set, an omitted property is a
        // malformed request. System.Text.Json presents both as null on the command, so only the
        // validator can tell the caller which one they sent.
        var productId = await SeedProductAsync();
        await AddAttributeAsync(productId, "Color", "Red");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new ReplaceProductAttributesRequest());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(productId)).Attributes.Should().HaveCount(1, "a rejected replace must change nothing");
    }

    [Test]
    public async Task ReplaceAttributes_WithADuplicateName_Is400()
    {
        var productId = await SeedProductAsync();

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new ReplaceProductAttributesRequest
            {
                Attributes =
                [
                    new ReplaceProductAttributeItem { Name = "Color", Value = "Red" },
                    new ReplaceProductAttributeItem { Name = "color", Value = "Blue" }
                ]
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(productId)).Attributes.Should().BeEmpty();
    }

    [Test]
    public async Task ReplaceAttributes_AboveTheCap_Is400()
    {
        var productId = await SeedProductAsync();

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new ReplaceProductAttributesRequest
            {
                Attributes = Enumerable.Range(0, 51)
                    .Select(i => new ReplaceProductAttributeItem { Name = $"Attr{i}", Value = "v" })
                    .ToList()
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ReplaceAttributes_OnAnUnknownProduct_Is404()
    {
        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{Guid.NewGuid()}/attributes",
            new ReplaceProductAttributesRequest { Attributes = [] });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Product.NotFound");
    }

    #endregion

    [Test]
    public async Task EditingAChild_InvalidatesTheCachedDetail()
    {
        // Without invalidation the edit stays invisible for the full TTL and the endpoint looks
        // like it silently did nothing.
        var productId = await SeedProductAsync();
        var attributeId = await AddAttributeAsync(productId, "Color", "Red");
        (await ReadAsync(productId)).Attributes.Single().Value.Should().Be("Red");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes/{attributeId}",
            new UpdateProductAttributeRequest { Name = "Color", Value = "Blue" });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadAsync(productId)).Attributes.Single().Value.Should().Be("Blue");
    }
}
