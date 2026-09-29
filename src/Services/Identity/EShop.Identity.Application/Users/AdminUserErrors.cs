using EShop.BuildingBlocks.Application;
using Microsoft.AspNetCore.Identity;

namespace EShop.Identity.Application.Users;

/// <summary>
/// Error codes for the admin user-management commands (Admin panel S7), in one place for the
/// reason <see cref="Roles.RoleErrors"/> exists: the controller maps codes to status codes, and
/// codes written out as literals at fourteen call sites drift.
/// </summary>
/// <remarks>
/// <c>User.NotFound</c> deliberately reuses the exact string <c>RoleErrors.UserNotFound</c> and
/// <c>GetAdminUserDetailsQueryHandler</c> already return, so "no such user" reads the same across
/// every Identity endpoint.
/// </remarks>
public static class AdminUserErrors
{
    public static readonly Error NotFound =
        new("User.NotFound", "User not found");

    public static readonly Error EmailConflict =
        new("User.EmailConflict", "Another account already uses this email address");

    public static readonly Error NotDeleted =
        new("User.NotDeleted", "This account is not deleted, so there is nothing to restore");

    public static readonly Error Deleted =
        new("User.Deleted", "This account is deleted. Restore it before changing it.");

    public static readonly Error TwoFactorNotEnabled =
        new("User.2FANotEnabled", "Two-factor authentication is not enabled for this account");

    public static readonly Error RoleNotFound =
        new("Role.NotFound", "Role not found");

    public static Error CreateFailed(string details) => new("User.CreateFailed", details);

    public static Error UpdateFailed(string details) => new("User.UpdateFailed", details);

    /// <summary>
    /// Throws when an <see cref="IdentityResult"/> fails <b>after the entity has already been
    /// mutated</b>, so that <c>TransactionBehavior</c> rolls the change back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one place these handlers deviate from the repo's usual "return a
    /// <c>Result</c>, don't throw" convention, and it is deliberate.
    /// <c>TransactionBehavior</c> commits on <b>any</b> non-exception return, a
    /// <c>Result</c> failure included — so a handler that sets <c>user.Email = …</c> and then
    /// returns <c>Result.Failure</c> because <c>UpdateAsync</c> rejected it <b>still persists the
    /// new email</b>. The change tracker holds the mutation whether or not
    /// <c>UserManager</c> chose to save it, and <c>UserManager.UpdateAsync</c> validates
    /// <i>before</i> saving, so the "rejected" path is exactly the one that leaves a dirty entity
    /// behind.
    /// </para>
    /// <para>
    /// Every failure an admin can actually cause — a duplicate email, an unknown role, a missing
    /// user — is therefore pre-checked <b>before</b> anything is mutated and returned as a
    /// <c>Result</c>. What is left here is a store-level rejection of an already-validated write,
    /// which is not an admin's mistake and must not be half-applied. Throwing is what the root
    /// guide prescribes for that case ("either it has to leave nothing written, or it has to
    /// throw").
    /// </para>
    /// </remarks>
    public static void EnsureSucceededAfterMutation(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
        throw new InvalidOperationException(
            $"{operation} failed after the entity was already modified, so the transaction is being rolled back: {errors}");
    }
}
