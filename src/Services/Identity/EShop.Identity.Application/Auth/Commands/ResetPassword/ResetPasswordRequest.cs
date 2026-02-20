namespace EShop.Identity.Application.Auth.Commands.ResetPassword;

/// <summary>
/// API request model for password reset
/// </summary>
public record ResetPasswordRequest
{
    public string UserId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}
