using System.Net.Http.Json;
using System.Text.Json;
using EShop.Basket.Application.Abstractions;
using EShop.Basket.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Basket.Infrastructure.Services;

public sealed class CatalogProductCatalogReader : IProductCatalogReader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CatalogProductCatalogReader> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogProductCatalogReader(
        HttpClient httpClient,
        IOptions<CatalogServiceOptions> options,
        ILogger<CatalogProductCatalogReader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        if (Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds));
    }

    public async Task<ProductCatalogSnapshot?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/v1/products/{productId}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CatalogProductResponse>(JsonOptions, cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            _logger.LogWarning("Catalog product payload is invalid for ProductId={ProductId}", productId);
            return null;
        }

        var effectivePrice = payload.DiscountPrice ?? payload.Price;
        return new ProductCatalogSnapshot(payload.Id, payload.Name, effectivePrice);
    }

    private sealed record CatalogProductResponse
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public decimal? DiscountPrice { get; init; }
    }
}
