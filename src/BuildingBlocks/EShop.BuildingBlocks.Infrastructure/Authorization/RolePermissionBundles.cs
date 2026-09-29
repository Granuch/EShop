namespace EShop.BuildingBlocks.Infrastructure.Authorization;

/// <summary>
/// Which permissions each role carries (decision Q4c: "permission claims with roles as bundles").
///
/// <para>
/// This map is the <b>only</b> place a role is turned into what it may do. Adding a
/// <c>Manager</c> or <c>Support</c> role later is an edit here plus a role row in Identity — no
/// endpoint changes, because endpoints name permissions rather than roles.
/// </para>
///
/// <para>
/// <b><c>Admin</c> must bundle every permission, and that is enforced by a test</b>
/// (<c>RolePermissionBundleTests</c>), not by remembering. The migration onto permissions is safe
/// precisely because of this invariant: every endpoint that reads
/// <c>RequireAuthorization("Admin")</c> today can move to a permission policy without any existing
/// administrator losing access. Break the invariant and the failure is silent — an admin simply
/// starts getting 403s on one endpoint.
/// </para>
/// </summary>
public static class RolePermissionBundles
{
    /// <summary>The single administrator role that exists today.</summary>
    public const string AdminRole = "Admin";

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Bundles =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Everything, by construction rather than by a hand-maintained list — a new permission
            // in EShopPermissions.All joins this bundle automatically, which is what keeps the
            // "Admin can do everything it could before" invariant true without anyone maintaining it.
            [AdminRole] = EShopPermissions.All.ToHashSet(StringComparer.Ordinal)
        };

    /// <summary>
    /// Whether <paramref name="role"/> carries <paramref name="permission"/>. An unknown role
    /// carries nothing — a role that exists in Identity but not here grants no permission, which is
    /// the safe direction.
    /// </summary>
    public static bool Grants(string role, string permission)
        => Bundles.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    /// <summary>The permissions <paramref name="role"/> carries, empty for an unknown role.</summary>
    public static IReadOnlySet<string> PermissionsFor(string role)
        => Bundles.TryGetValue(role, out var permissions)
            ? permissions
            : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Every role this map knows about.</summary>
    public static IReadOnlyCollection<string> KnownRoles => (IReadOnlyCollection<string>)Bundles.Keys;
}
