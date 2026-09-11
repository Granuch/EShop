namespace EShop.Ordering.Infrastructure.Configuration;

/// <summary>
/// Where Ordering reads product names and prices from. Same section name and shape as Basket's.
/// <see cref="BaseUrl"/> is validated at startup, so a deployment that forgets it fails to boot
/// instead of failing its first order.
/// </summary>
public sealed class CatalogServiceOptions
{
    public const string SectionName = "CatalogService";

    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
}
