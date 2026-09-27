using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Fixtures;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Products;

/// <summary>
/// M1 / A6 (Admin panel S3), the database side of "an attribute name is unique within a product".
/// With the aggregate's in-memory dedupe blinded (<see cref="BlindAttributeCheckApiFactory"/>), only
/// the unique index <c>IX_ProductAttributes_ProductId_Name</c> stands between a request and a
/// duplicate — so these pin what the index enforces and how a violation is reported, independently
/// of the check that normally answers first and hides both.
/// </summary>
/// <remarks>
/// Before this stage there was no index at all: dedupe existed only in
/// <c>Product.AddAttribute</c>, against the attributes that one request happened to load, so two
/// concurrent requests could both pass and both insert.
/// </remarks>
[TestFixture]
[Category("Integration")]
[Category("Postgres")]
public class ProductAttributeConflictTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    protected override async Task<CatalogApiFactory> CreateFactoryAsync()
        => await BlindAttributeCheckApiFactory.CreateAsync();

    private async Task<Guid> SeedProductAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        return await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Attribute conflict", CatalogDataHelper.GenerateUniqueSku("ATTRC"), 20m, 10, categoryId);
    }

    private Task<HttpResponseMessage> PostAttributeAsync(Guid productId, string name, string value)
        => Client.PostAsJsonAsync($"{ProductsEndpoint}/{productId}/attributes",
            new AddProductAttributeRequest { Name = name, Value = value });

    [Test]
    public async Task ADuplicateNameReachingTheIndex_IsA409AttributeConflict()
    {
        var productId = await SeedProductAsync();

        (await PostAttributeAsync(productId, "Color", "Red")).StatusCode
            .Should().Be(HttpStatusCode.Created);

        using var response = await PostAttributeAsync(productId, "Color", "Blue");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Product.AttributeConflict",
                "AddProductAttributeConflict must claim the violation before AddEfDuplicateKey reports a generic DuplicateResource");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await db.ProductAttributes.CountAsync(a => a.ProductId == productId)).Should().Be(1,
            "the rejected write must not have persisted");
    }

    [Test]
    public async Task TheIndexIsCaseInsensitive()
    {
        // The reason M1 indexes lower("Name") rather than "Name". A plain unique index would accept
        // this pair under Postgres' default collation, leaving the race half open — the aggregate
        // refuses "Color"/"color", so the backstop must refuse it too or the two disagree.
        var productId = await SeedProductAsync();

        (await PostAttributeAsync(productId, "Color", "Red")).StatusCode
            .Should().Be(HttpStatusCode.Created);

        using var response = await PostAttributeAsync(productId, "COLOR", "Blue");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>())!.ErrorCode
            .Should().Be("Product.AttributeConflict");
    }

    [Test]
    public async Task TheIndexIsScopedToOneProduct()
    {
        // Uniqueness is per product, not global — two products may each have a "Color".
        var first = await SeedProductAsync();
        var second = await SeedProductAsync();

        (await PostAttributeAsync(first, "Color", "Red")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAttributeAsync(second, "Color", "Blue")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task DifferentNamesOnOneProduct_AreFine()
    {
        var productId = await SeedProductAsync();

        (await PostAttributeAsync(productId, "Color", "Red")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAttributeAsync(productId, "Size", "L")).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
