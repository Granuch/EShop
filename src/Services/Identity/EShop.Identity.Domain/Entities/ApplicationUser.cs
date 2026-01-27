using Microsoft.AspNetCore.Identity;

namespace EShop.Identity.Domain.Entities;

/// <summary>
/// Extended user entity with additional properties
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }

    // Account status
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // OAuth integration
    public string? GoogleId { get; set; }
    public string? GitHubId { get; set; }

    // 2FA
    public string? TwoFactorSecret { get; set; }

    // Computed property
    public string FullName => $"{FirstName} {LastName}".Trim();

    // TODO: Add email confirmation token expiration
    // TODO: Add password reset token with expiration
    // TODO: Add account lockout tracking
}
