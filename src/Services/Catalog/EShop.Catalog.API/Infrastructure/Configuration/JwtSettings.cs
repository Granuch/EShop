namespace EShop.Catalog.API.Infrastructure.Configuration;

/// <summary>
/// JWT configuration settings for token validation.
/// Catalog Service only validates tokens (does not issue them).
/// </summary>
public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
