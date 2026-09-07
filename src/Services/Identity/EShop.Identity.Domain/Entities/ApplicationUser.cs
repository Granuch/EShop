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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }

    // Account status.
    //
    // These three were independent public setters, which made incoherent states reachable and
    // representable: deleted-but-active, active-with-a-DeletedAt, deleted-with-no-DeletedAt.
    // The only method that set them coherently (UserRepository.DeleteAsync) had no caller, and
    // there was no query filter, so six handlers re-checked IsDeleted by hand and five omitted
    // it. Setters are private now and the transitions go through the methods below, so the
    // three fields can only move together.
    public bool IsActive { get; private set; } = true;
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// Soft-deletes the account: deleted, inactive, and stamped, in one step. Idempotent — a
    /// second call keeps the original <see cref="DeletedAt"/> so the audit trail is not moved.
    /// </summary>
    public void SoftDelete()
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        IsActive = false;
    }

    /// <summary>
    /// Disables an account without deleting it — the "suspended" state, distinct from deleted.
    /// The global query filter only hides deleted users, so a deactivated one is still found
    /// and still gets the explicit <c>Auth.AccountDisabled</c> answer.
    /// </summary>
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Re-enables a deactivated account. Deliberately refuses to resurrect a deleted one:
    /// undeleting is not a state transition this model supports.
    /// </summary>
    public void Activate()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A soft-deleted account cannot be reactivated.");
        }

        IsActive = true;
    }

    // OAuth integration
    public string? GoogleId { get; set; }
    public string? GitHubId { get; set; }

    // 2FA
    public string? TwoFactorSecret { get; set; }

    // Computed property
    public string FullName => $"{FirstName} {LastName}".Trim();
}
