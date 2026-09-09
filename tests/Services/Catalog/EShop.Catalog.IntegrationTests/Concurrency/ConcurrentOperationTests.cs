using System.Net;
using System.Net.Http.Json;
using EShop.Catalog.Infrastructure.Data;
using EShop.Catalog.IntegrationTests.Helpers;
using EShop.Catalog.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Catalog.IntegrationTests.Concurrency;

/// <summary>
/// Integration tests for concurrent operations
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Concurrency")]
public class ConcurrentOperationTests : AuthenticatedIntegrationTestBase
{
    private const string ProductsEndpoint = "/api/v1/products";

    [Test]
    public async Task ConcurrentReads_SameProduct_ShouldAllSucceed()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var productId = await CatalogDataHelper.CreateProductAsync(
            scope.ServiceProvider, "Concurrent Read Product", CatalogDataHelper.GenerateUniqueSku("CR"), 29.99m, 10, categoryId);

        // Act — Simulate 10 concurrent GET requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Client.GetAsync($"{ProductsEndpoint}/{productId}"));

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Test]
    public async Task ConcurrentProductCreations_UniqueSku_ShouldAllSucceed()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);

        // Act — Create 5 products concurrently with unique SKUs
        var tasks = Enumerable.Range(0, 5)
            .Select(i => Client.PostAsJsonAsync(ProductsEndpoint, new CreateProductRequest
            {
                Name = $"Concurrent Product {i}",
                Sku = CatalogDataHelper.GenerateUniqueSku("CONC"),
                Price = 10m + i,
                StockQuantity = i * 10,
                CategoryId = categoryId
            }));

        var responses = await Task.WhenAll(tasks);

        // Assert
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        successCount.Should().Be(5, "all concurrent creations with unique SKUs should succeed");
    }

    /// <summary>
    /// M15. This test used to issue its two POSTs <b>sequentially</b> and assert that the
    /// application-level check rejected the second — a scenario <c>CreateProductTests</c> already
    /// covered. It was the only test standing in for SKU uniqueness under concurrency and it
    /// exercised none, which is a large part of why the missing unique index went unnoticed for
    /// five migrations. Its <c>[Description]</c> even said InMemory could not enforce unique
    /// constraints, which was true and was treated as a reason to test something else.
    ///
    /// <para>
    /// It now fires the creates together. Both statuses are legitimate and which one you get is a
    /// race: 400 <c>Product.SkuConflict</c> when the loser's read-then-write saw the winner's row,
    /// 409 <c>Product.SkuConflict</c> when it did not and the partial unique index caught it at
    /// insert. What must never happen — and did, before Stage 1 — is two 201s.
    /// </para>
    /// </summary>
    [Test]
    public async Task ConcurrentProductCreations_SameSku_ShouldCreateExactlyOne()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var sameSku = CatalogDataHelper.GenerateUniqueSku("SAME");

        // Act — eight genuinely concurrent creates, all claiming the same SKU.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => Client.PostAsJsonAsync(ProductsEndpoint, new CreateProductRequest
            {
                Name = $"Racing Product {i}",
                Sku = sameSku,
                Price = 10m + i,
                StockQuantity = 5,
                CategoryId = categoryId
            })));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1,
            "exactly one concurrent create may win a contested SKU");

        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(
                r => r.StatusCode == HttpStatusCode.BadRequest || r.StatusCode == HttpStatusCode.Conflict,
                "a loser is either rejected by the pre-check (400) or by the unique index (409) — "
                + "never by a 500, which is what an unmapped DbUpdateException would produce");

        // The status codes alone would still pass if the database had let a second row through and
        // the response merely reported an error, so assert the stored state too.
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var liveWithSku = await db.Products.CountAsync(p => p.Sku == sameSku);
        liveWithSku.Should().Be(1, "the losing writes must not have persisted");
    }

    /// <summary>
    /// The other half of the partial index's contract: <c>WHERE NOT "IsDeleted"</c> exists so a
    /// discontinued product does not burn its SKU forever. A total unique index would make this
    /// 409 while <c>CreateProductCommandHandler</c>'s pre-check — which runs under the
    /// <c>!p.IsDeleted</c> global query filter — reported the SKU as free.
    /// </summary>
    [Test]
    public async Task CreatingAProduct_WithASoftDeletedProductsSku_ShouldSucceed()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var categoryId = await CatalogDataHelper.GetFirstCategoryIdAsync(scope.ServiceProvider);
        var sku = CatalogDataHelper.GenerateUniqueSku("REUSE");

        var create = await Client.PostAsJsonAsync(ProductsEndpoint, new CreateProductRequest
        {
            Name = "Discontinued Product",
            Sku = sku,
            Price = 10m,
            StockQuantity = 1,
            CategoryId = categoryId
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var productId = (await create.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        (await Client.DeleteAsync($"{ProductsEndpoint}/{productId}"))
            .IsSuccessStatusCode.Should().BeTrue();

        // Act — the SKU should now be free again.
        var recreate = await Client.PostAsJsonAsync(ProductsEndpoint, new CreateProductRequest
        {
            Name = "Replacement Product",
            Sku = sku,
            Price = 12m,
            StockQuantity = 3,
            CategoryId = categoryId
        });

        // Assert
        recreate.StatusCode.Should().Be(HttpStatusCode.Created,
            "the unique index is partial, so a soft-deleted product releases its SKU");
    }

    [Test]
    public async Task ConcurrentListQueries_ShouldAllSucceed()
    {
        // Act — Simulate 10 concurrent list queries with different filters
        var tasks = Enumerable.Range(0, 10)
            .Select(i => Client.GetAsync($"{ProductsEndpoint}?PageNumber={i + 1}&PageSize=5"));

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}
