namespace EShop.Identity.Domain.ValueObjects;

/// <summary>
/// Value object representing a refresh token
/// </summary>
public record RefreshToken
{
    public string Token { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string? CreatedByIp { get; init; }
    public DateTime? RevokedAt { get; init; }
    public string? RevokedByIp { get; init; }
    public string? ReplacedByToken { get; init; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    // TODO: Implement token rotation strategy
    // TODO: Add device information tracking
}
