namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Turns a user's roles into the permissions they grant, so the login response and the profile can
/// tell a client what the caller may do (frontend-contracts F-07).
///
/// <para>
/// <b>Why this interface lives in Domain.</b> The role-to-permission map is
/// <c>RolePermissionBundles</c> in <c>BuildingBlocks.Infrastructure</c>, the same table every
/// service's <c>PermissionAuthorizationHandler</c> authorizes against. Application does not reference
/// Infrastructure, so it reaches the map through this abstraction, as it reaches
/// <see cref="ICachedUserRolesService"/>. Keeping one table is the point: a second copy of the map in
/// Identity would let the permissions a client is shown drift from the permissions it is granted.
/// </para>
/// </summary>
public interface IRolePermissionResolver
{
    /// <summary>
    /// Every permission any of <paramref name="roles"/> grants, each once, in the vocabulary's own
    /// order (<c>EShopPermissions.All</c>). Role names match case-insensitively, as the authorization
    /// handler matches them; a role with no bundle grants nothing.
    /// </summary>
    IReadOnlyList<string> PermissionsFor(IEnumerable<string> roles);
}
