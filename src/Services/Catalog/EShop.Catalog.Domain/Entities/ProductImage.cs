using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Product image entity
/// </summary>
public class ProductImage : Entity<Guid>
{
    private static readonly HashSet<string> AllowedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif"
    ];

    public Guid ProductId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string? AltText { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsMain { get; private set; }

    private ProductImage() { }

    public ProductImage(Guid productId, string url, string? altText, int displayOrder)
    {
        if (productId == Guid.Empty)
            throw new DomainException("Product image requires a valid product id.");

        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        var normalizedUrl = NormalizeAndValidateUrl(url);
        var normalizedAltText = NormalizeAltText(altText);

        Id = Guid.NewGuid();
        ProductId = productId;
        Url = normalizedUrl;
        AltText = normalizedAltText;
        DisplayOrder = displayOrder;
        IsMain = false;
        CreatedAt = DateTime.UtcNow;
    }

    public void SetAsMain()
    {
        IsMain = true;
    }

    public void UnsetAsMain()
    {
        IsMain = false;
    }

    private static string NormalizeAndValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("Product image URL cannot be empty.");

        var normalizedUrl = url.Trim();
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var parsedUrl)
            || (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("Product image URL must be an absolute HTTP/HTTPS URL.");
        }

        var extension = Path.GetExtension(parsedUrl.AbsolutePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new DomainException("Product image format is not supported.");
        }

        return normalizedUrl;
    }

    private static string? NormalizeAltText(string? altText)
    {
        if (string.IsNullOrWhiteSpace(altText))
            return null;

        var normalized = altText.Trim();
        if (normalized.Length > 200)
            throw new DomainException("Product image alt text must be 200 characters or fewer.");

        return normalized;
    }
}
