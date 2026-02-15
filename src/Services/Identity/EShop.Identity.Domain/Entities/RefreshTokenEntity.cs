namespace EShop.Identity.Domain.Entities;

/// <summary>
/// Refresh token entity for database storage
/// </summary>
public class RefreshTokenEntity
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? RevokeReason { get; set; }

    /// <summary>
    /// Optimistic concurrency token for race condition protection during token rotation.
    /// EF Core will include this in WHERE clause of UPDATE statements.
    /// </summary>
    public uint Version { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    // Navigation property
    public ApplicationUser User { get; set; } = null!;
}
