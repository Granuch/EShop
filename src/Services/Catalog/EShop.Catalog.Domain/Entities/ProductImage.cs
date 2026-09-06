using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Product image entity
/// </summary>
public class ProductImage : Entity<Guid>
{
    private const int MaxUrlLength = 500;

    public Guid ProductId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string? AltText { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsMain { get; private set; }

    private ProductImage() { }

    internal ProductImage(Guid productId, string url, string? altText, int displayOrder)
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

    internal void SetAsMain()
    {
        IsMain = true;
    }

    internal void UnsetAsMain()
    {
        IsMain = false;
    }

    /// <summary>
    /// Enforces only: non-empty, absolute HTTP/HTTPS, and no longer than the
    /// <see cref="MaxUrlLength"/> of the Url column.
    /// </summary>
    /// <remarks>
    /// The file-extension allowlist that used to live here was removed deliberately, to admit
    /// extensionless CDN links. The consequence is accepted, not overlooked: nothing in the domain
    /// asserts the URL points at an actual image any more, so a mistyped link fails visually at
    /// render time instead of at the API boundary. Validating that would require a network call
    /// from the domain; the responsibility sits with the admin client instead.
    /// </remarks>
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

        if (normalizedUrl.Length > MaxUrlLength)
            throw new DomainException($"Product image URL must be {MaxUrlLength} characters or fewer.");

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
