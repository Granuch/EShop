namespace EShop.Identity.Application.Auth.Commands.RevokeToken;

/// <summary>
/// API request model for revoking a refresh token
/// </summary>
public record RevokeTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}
