namespace EShop.Identity.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// API request model for forgot password
/// </summary>
public record ForgotPasswordRequest
{
    public string Email { get; init; } = string.Empty;
}
