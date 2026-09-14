using System.Net;
using System.Text;
using EShop.Ordering.Domain.Interfaces;
using EShop.Ordering.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EShop.Ordering.UnitTests.Infrastructure;

/// <summary>
/// The reader is the one place an order line's price enters Ordering, so what it does with each kind
/// of Catalog answer is the contract: a product (effective price), a 404 (no such product), and
/// everything else (Catalog unavailable — never "no such product", which would turn an outage into a
/// client error).
/// </summary>
[TestFixture]
public class CatalogProductCatalogReaderTests
{
    private static readonly Guid ProductId = Guid.NewGuid();

    private static (CatalogProductCatalogReader Reader, StubHandler Handler) ReaderAnswering(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://catalog.test/") };
        return (new CatalogProductCatalogReader(client, NullLogger<CatalogProductCatalogReader>.Instance), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Test]
    public async Task AProduct_IsPricedAtItsDiscountWhenItHasOne()
    {
        var (reader, handler) = ReaderAnswering(_ =>
            Json($$"""{"id":"{{ProductId}}","name":"Widget","price":20.00,"discountPrice":15.00}"""));

        var product = await reader.GetByIdAsync(ProductId);

        Assert.That(product, Is.EqualTo(new CatalogProduct(ProductId, "Widget", 15.00m)));
        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo($"/api/v1/products/{ProductId}"));
    }

    [Test]
    public async Task AProduct_WithoutADiscount_IsPricedAtItsListPrice()
    {
        var (reader, _) = ReaderAnswering(_ =>
            Json($$"""{"id":"{{ProductId}}","name":"Widget","price":20.00,"discountPrice":null}"""));

        var product = await reader.GetByIdAsync(ProductId);

        Assert.That(product!.UnitPrice, Is.EqualTo(20.00m));
    }

    [Test]
    public async Task A404_MeansNoSuchProduct()
    {
        var (reader, _) = ReaderAnswering(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.That(await reader.GetByIdAsync(ProductId), Is.Null);
    }

    [Test]
    public void A500_IsCatalogUnavailable_NotNoSuchProduct()
    {
        var (reader, _) = ReaderAnswering(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.ThrowsAsync<CatalogUnavailableException>(() => reader.GetByIdAsync(ProductId));
    }

    [Test]
    public void AConnectionFailure_IsCatalogUnavailable()
    {
        var (reader, _) = ReaderAnswering(_ => throw new HttpRequestException("connection refused"));

        Assert.ThrowsAsync<CatalogUnavailableException>(() => reader.GetByIdAsync(ProductId));
    }

    [Test]
    public void APayloadForAnotherProduct_IsCatalogUnavailable()
    {
        var (reader, _) = ReaderAnswering(_ =>
            Json($$"""{"id":"{{Guid.NewGuid()}}","name":"Someone Else","price":1.00}"""));

        Assert.ThrowsAsync<CatalogUnavailableException>(() => reader.GetByIdAsync(ProductId));
    }

    [Test]
    public void ABodyThatIsNotJson_IsCatalogUnavailable()
    {
        var (reader, _) = ReaderAnswering(_ => Json("<html>gateway error</html>"));

        Assert.ThrowsAsync<CatalogUnavailableException>(() => reader.GetByIdAsync(ProductId));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }
}
