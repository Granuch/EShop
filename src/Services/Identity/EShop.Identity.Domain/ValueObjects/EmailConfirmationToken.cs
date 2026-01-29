namespace EShop.Identity.Domain.ValueObjects;

/// <summary>
/// Value object for email confirmation tokens
/// </summary>
public record EmailConfirmationToken
{
    public string Token { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
