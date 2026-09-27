using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Identity.Domain.Interfaces;

namespace EShop.Identity.Infrastructure.Services;

/// <summary>
/// Reads <see cref="RolePermissionBundles"/>, the table every service authorizes against, so the
/// permissions Identity reports cannot disagree with the permissions a service grants. Stateless,
/// hence a singleton.
/// </summary>
public sealed class RolePermissionResolver : IRolePermissionResolver
{
    public IReadOnlyList<string> PermissionsFor(IEnumerable<string> roles)
    {
        var roleList = roles.ToList();

        // Walking the vocabulary rather than the bundles keeps the order stable and removes
        // duplicates when two roles grant the same permission.
        return EShopPermissions.All
            .Where(permission => roleList.Any(role => RolePermissionBundles.Grants(role, permission)))
            .ToList();
    }
}
