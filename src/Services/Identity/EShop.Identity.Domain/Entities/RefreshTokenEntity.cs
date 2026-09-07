namespace EShop.Identity.Domain.Entities;

/// <summary>
/// Refresh token entity for database storage
/// </summary>
public class RefreshTokenEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 of the refresh token, lowercase hex (see <c>RefreshTokenHasher</c>).
    /// The token itself is returned to the client once and never persisted — storing it raw
    /// made a read of this table equivalent to every live session's credentials (SEC-04).
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }

    /// <summary>
    /// Hash of the token that superseded this one on rotation. Hashed for the same reason as
    /// <see cref="TokenHash"/>: this column previously held the *new* token in plaintext, so the
    /// rotation chain leaked every successor token as well as the current one.
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

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
