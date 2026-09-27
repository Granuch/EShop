namespace EShop.Notification.API.Configuration;

/// <summary>
/// The token settings this service validates against (Admin panel S12). A local copy, like every other service's —
/// the shared piece is <c>JwtSecretGuard</c>, which checks the key, not this binding class.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
