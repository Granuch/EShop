namespace EShop.Basket.Infrastructure.Configuration;

public sealed class CatalogServiceOptions
{
    public const string SectionName = "CatalogService";

    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
}
