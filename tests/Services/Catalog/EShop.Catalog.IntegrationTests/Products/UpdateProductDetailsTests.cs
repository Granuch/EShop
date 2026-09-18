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
/// Admin panel S2 — the fields <c>PUT /api/v1/products/{id}</c> gained: name, description, SKU and
/// category. Until this stage the command carried three fields (id, price, stock), so the admin
/// "edit product" form could change nothing else.
///
/// <para>
/// The endpoint keeps its existing status mapping — everything that is not success is a 400,
/// including <c>Product.NotFound</c> — because <c>UpdateProduct_WithNonExistentId_ShouldReturnBadRequest</c>
/// pins it and POST behaves the same way. Only a SKU race that reaches the unique index answers 409,
/// through <c>AddProductSkuConflict()</c>.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class UpdateProductDetailsTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    private async Task<(Guid ProductId, Guid CategoryId)> SeedAsync(string name, string? description = null)
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, name, CatalogDataHelper.GenerateUniqueSku("S2"), 20m, 10, categoryId);

        if (description is not null)
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var stored = await db.Products.SingleAsync(p => p.Id == productId);
            stored.UpdateDetails(stored.Name, description, stored.Sku);
            await db.SaveChangesAsync();
        }

        return (productId, categoryId);
    }

    private async Task<ProductDetailsResponse> ReadAsync(Guid productId)
    {
        var response = await Client.GetAsync($"{ProductsEndpoint}/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.Content.ReadFromJsonAsync<ProductDetailsResponse>();
        product.Should().NotBeNull();
        return product!;
    }

    [Test]
    public async Task Update_ChangesNameDescriptionAndSku()
    {
        var (productId, categoryId) = await SeedAsync("Before");
        var newSku = CatalogDataHelper.GenerateUniqueSku("S2NEW");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 25m,
            StockQuantity = 7,
            Name = "After",
            Description = "A description",
            Sku = newSku,
            CategoryId = categoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var product = await ReadAsync(productId);
        product.Name.Should().Be("After");
        product.Description.Should().Be("A description");
        product.Sku.Should().Be(newSku);
        product.Price.Should().Be(25m);
        product.StockQuantity.Should().Be(7);
    }

    [Test]
    public async Task Update_WithoutTheNewFields_LeavesThemAlone()
    {
        // The compatibility case: the body this endpoint has always accepted must keep behaving
        // exactly as before, or every existing caller breaks the day the fields are added.
        var (productId, _) = await SeedAsync("Untouched", "Original description");
        var before = await ReadAsync(productId);

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 33m,
            StockQuantity = 3
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await ReadAsync(productId);
        after.Name.Should().Be(before.Name);
        after.Description.Should().Be("Original description");
        after.Sku.Should().Be(before.Sku);
        after.CategoryId.Should().Be(before.CategoryId);
        after.Price.Should().Be(33m);
        after.StockQuantity.Should().Be(3);
    }

    [Test]
    public async Task Update_WithABlankDescription_ClearsIt()
    {
        var (productId, _) = await SeedAsync("Clearable", "Goes away");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 20m,
            StockQuantity = 10,
            Description = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync(productId)).Description.Should().BeNull();
    }

    [Test]
    public async Task Update_MovesTheProductToAnotherCategory()
    {
        var (productId, originalCategoryId) = await SeedAsync("Movable");

        Guid targetCategoryId;
        using (var scope = Factory.Services.CreateScope())
        {
            targetCategoryId = await CatalogDataHelper.CreateCategoryAsync(
                scope.ServiceProvider, $"S2 target {Guid.NewGuid():N}");
        }

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 20m,
            StockQuantity = 10,
            CategoryId = targetCategoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var product = await ReadAsync(productId);
        product.CategoryId.Should().Be(targetCategoryId);
        product.CategoryId.Should().NotBe(originalCategoryId);
    }

    [Test]
    public async Task Update_ToAMissingCategory_IsRefused_AndChangesNothing()
    {
        var (productId, originalCategoryId) = await SeedAsync("Stays put");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 99m,
            StockQuantity = 1,
            Name = "Should not stick",
            CategoryId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Category.NotFound");

        // The assertion that matters: TransactionBehavior commits on a failure Result, so a handler
        // that mutated before checking would have persisted the name and price anyway.
        var product = await ReadAsync(productId);
        product.Name.Should().Be("Stays put");
        product.Price.Should().Be(20m);
        product.CategoryId.Should().Be(originalCategoryId);
    }

    [Test]
    public async Task Update_ToASkuAnotherProductHolds_IsRefused_AndChangesNothing()
    {
        var (productId, _) = await SeedAsync("Keeps its sku");
        var (otherId, _) = await SeedAsync("Owns the sku");
        var takenSku = (await ReadAsync(otherId)).Sku;

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 77m,
            StockQuantity = 2,
            Sku = takenSku
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
        problem!.ErrorCode.Should().Be("Product.SkuConflict");

        var product = await ReadAsync(productId);
        product.Sku.Should().NotBe(takenSku);
        product.Price.Should().Be(20m);
    }

    [Test]
    public async Task Update_ResendingItsOwnSku_IsAllowed()
    {
        // The self-collision case: a pre-check that forgot to exclude this product would refuse
        // every admin form submission that did not happen to change the SKU.
        var (productId, _) = await SeedAsync("Same sku");
        var ownSku = (await ReadAsync(productId)).Sku;

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 44m,
            StockQuantity = 4,
            Name = "Renamed",
            Sku = ownSku
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var product = await ReadAsync(productId);
        product.Sku.Should().Be(ownSku);
        product.Name.Should().Be("Renamed");
    }

    [Test]
    public async Task Update_WithABlankName_IsRejectedByValidation()
    {
        var (productId, _) = await SeedAsync("Named");

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 20m,
            StockQuantity = 10,
            Name = "   "
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();

        // "ValidationError", not "Validation.Failed", and the difference is structural rather than
        // cosmetic: ValidationBehavior only converts a failure into a Result when TResponse is the
        // GENERIC Result<T>. UpdateProductCommand returns the non-generic Result, so validation
        // falls through to `throw new ValidationException(...)` and is mapped by AddCommon() instead
        // — which means ProductEndpoints.ProblemForError's "Validation.Failed" discrimination can
        // never fire for a command shaped like this one.
        problem!.ErrorCode.Should().Be("ValidationError");
    }

    [Test]
    public async Task Update_InvalidatesTheCachedDetailAndList()
    {
        // Reads populate both caches first; without invalidation the rename stays invisible for the
        // full five-minute TTL and the endpoint looks like it silently did nothing.
        var (productId, _) = await SeedAsync("Cached name");
        await ReadAsync(productId);
        (await Client.GetAsync($"{ProductsEndpoint}?PageSize=50")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.PutAsJsonAsync($"{ProductsEndpoint}/{productId}", new UpdateProductRequest
        {
            ProductId = productId,
            Price = 20m,
            StockQuantity = 10,
            Name = "Renamed after caching"
        });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReadAsync(productId)).Name.Should().Be("Renamed after caching");

        var listResponse = await Client.GetAsync($"{ProductsEndpoint}?PageSize=50");
        var body = await listResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Renamed after caching");
        body.Should().NotContain("Cached name");
    }
}
