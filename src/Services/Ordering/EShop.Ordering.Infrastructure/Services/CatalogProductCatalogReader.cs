using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Ordering.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EShop.Ordering.Infrastructure.Services;

/// <summary>
/// Reads a product from Catalog's public <c>GET api/v1/products/{id}</c>, anonymously — so an
/// unpublished or deleted product is a 404 here exactly as it is for a shopper. Mirrors Basket's
/// <c>CatalogProductCatalogReader</c>, including the effective-price rule (<c>DiscountPrice ?? Price</c>).
///
/// <para>
/// Every way Catalog can fail to answer becomes <see cref="CatalogUnavailableException"/>, which the
/// handlers report as 503; only a 404 means "no such product". A 200 whose body names a different
/// product, or no name at all, is Catalog misbehaving rather than the product not existing, so it is
/// treated as unavailable too.
/// </para>
/// </summary>
public sealed class CatalogProductCatalogReader : IProductCatalogReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CatalogProductCatalogReader> _logger;

    public CatalogProductCatalogReader(HttpClient httpClient, ILogger<CatalogProductCatalogReader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CatalogProduct?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync($"api/v1/products/{productId}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(productId, "the request failed", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(productId, "the request timed out", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Unavailable(productId, $"Catalog answered {(int)response.StatusCode}");
            }

            CatalogProductResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<CatalogProductResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw Unavailable(productId, "the response body was not a product", ex);
            }

            if (payload is null || payload.Id != productId || string.IsNullOrWhiteSpace(payload.Name))
            {
                throw Unavailable(productId, "the response body did not describe the requested product");
            }

            return new CatalogProduct(payload.Id, payload.Name, payload.DiscountPrice ?? payload.Price);
        }
    }

    private CatalogUnavailableException Unavailable(Guid productId, string reason, Exception? inner = null)
    {
        _logger.LogWarning(inner, "Catalog lookup for ProductId={ProductId} failed: {Reason}", productId, reason);
        return new CatalogUnavailableException($"Catalog lookup for product '{productId}' failed: {reason}.", inner);
    }

    /// <summary>The subset of Catalog's <c>ProductDetailsDto</c> an order line needs.</summary>
    private sealed record CatalogProductResponse
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public decimal? DiscountPrice { get; init; }
    }
}
