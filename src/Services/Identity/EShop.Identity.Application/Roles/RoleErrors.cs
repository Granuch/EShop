using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Roles;

/// <summary>
/// Error codes for the role endpoints, in one place.
///
/// These used to be thirteen hardcoded string literals scattered through
/// <c>RolesController</c>, because the controller had no CQRS layer and so had no
/// <c>Result</c> to unwrap. That is what made the codes easy to typo and impossible to grep
/// as a set. Every code here is the one the controller already returned — this is a move, not
/// a contract change.
/// </summary>
public static class RoleErrors
{
    public static readonly Error NotFound =
        new("Role.NotFound", "Role not found");

    public static readonly Error UserNotFound =
        new("User.NotFound", "User not found");

    public static readonly Error AlreadyExists =
        new("Role.Exists", "Role already exists");

    public static readonly Error CannotDeleteSystemRole =
        new("Role.CannotDelete", "Cannot delete system roles");

    public static Error CreateFailed(string details) => new("Role.CreateFailed", details);

    public static Error UpdateFailed(string details) => new("Role.UpdateFailed", details);

    public static Error DeleteFailed(string details) => new("Role.DeleteFailed", details);

    public static Error AddUserFailed(string details) => new("Role.AddUserFailed", details);

    public static Error RemoveUserFailed(string details) => new("Role.RemoveUserFailed", details);
}
