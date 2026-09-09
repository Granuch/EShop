using System.Net;
using System.Text;
using EShop.Basket.Infrastructure.Configuration;
using EShop.Basket.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EShop.Basket.UnitTests.Infrastructure;

/// <summary>
/// The Catalog → Basket price contract (H5b / D3).
///
/// <para>
/// <c>CatalogProductCatalogReader</c> is where a basket item's price comes from, and it prices a
/// product as <c>DiscountPrice ?? Price</c>. That coalesce was a <b>dead branch</b> until Stage 5:
/// Catalog had no way to set <c>DiscountPrice</c>, so it was always null and the reader always used
/// the list price. Now that Catalog can set it, this is live pricing logic in another bounded
/// context and it had no test at all.
/// </para>
///
/// <para>
/// The payloads below are the JSON Catalog's <c>GET /api/v1/products/{id}</c> actually returns —
/// camelCase, with <c>discountPrice</c> either a number or <c>null</c>. That is the part worth
/// pinning: this reader deserializes into a private record of its own, so a field renamed on
/// Catalog's <c>ProductDetailsDto</c> would not break the build here, it would silently bind to
/// <c>null</c> and quietly charge the list price.
/// </para>
/// </summary>
[TestFixture]
public class CatalogProductCatalogReaderTests
{
    private static CatalogProductCatalogReader CreateReader(HttpStatusCode status, string json)
    {
        var handler = new StubHandler(status, json);
        var httpClient = new HttpClient(handler);

        var options = Options.Create(new CatalogServiceOptions
        {
            BaseUrl = "http://catalog-api.test/",
            TimeoutSeconds = 5
        });

        return new CatalogProductCatalogReader(
            httpClient,
            options,
            NullLogger<CatalogProductCatalogReader>.Instance);
    }

    private static string ProductJson(Guid id, decimal price, string? discountPrice)
        => $$"""
        {
          "id": "{{id}}",
          "name": "Discounted Product",
          "sku": "SKU-001",
          "price": {{price}},
          "discountPrice": {{discountPrice ?? "null"}},
          "stockQuantity": 10,
          "status": 1,
          "categoryId": "{{Guid.NewGuid()}}"
        }
        """;

    [Test]
    public async Task GetByIdAsync_WithADiscount_ShouldPriceAtTheDiscount()
    {
        var id = Guid.NewGuid();
        var reader = CreateReader(HttpStatusCode.OK, ProductJson(id, 100m, "79.99"));

        var snapshot = await reader.GetByIdAsync(id);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot!.Price, Is.EqualTo(79.99m),
            "a basket item must be priced at what the customer pays, not at the list price");
    }

    [Test]
    public async Task GetByIdAsync_WithoutADiscount_ShouldPriceAtTheListPrice()
    {
        var id = Guid.NewGuid();
        var reader = CreateReader(HttpStatusCode.OK, ProductJson(id, 100m, discountPrice: null));

        var snapshot = await reader.GetByIdAsync(id);

        Assert.That(snapshot!.Price, Is.EqualTo(100m));
    }

    [Test]
    public async Task GetByIdAsync_WhenCatalogReturns404_ShouldReturnNull()
    {
        var reader = CreateReader(HttpStatusCode.NotFound, string.Empty);

        var snapshot = await reader.GetByIdAsync(Guid.NewGuid());

        Assert.That(snapshot, Is.Null);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        public StubHandler(HttpStatusCode status, string json)
        {
            _status = status;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }
}
