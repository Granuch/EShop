using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace EShop.BuildingBlocks.Infrastructure.Authorization;

/// <summary>
/// Requires one named permission. The policy registered for a permission carries exactly this and
/// nothing else — in particular <b>no</b> <c>RequireAuthenticatedUser()</c>, which is deliberate and
/// matches Identity's <c>InternalService</c> policy: the authorization middleware already answers
/// 401 to an unauthenticated caller and 403 to an authenticated one that fails the requirement, so
/// adding the authentication requirement would only turn some 403s into 401s and tell an anonymous
/// prober that the resource exists.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    public string Permission { get; }

    public override string ToString() => $"Permission:{Permission}";
}

/// <summary>
/// Grants a <see cref="PermissionRequirement"/> from either source, in this order:
/// <list type="number">
/// <item>a <c>permission</c> claim carrying the value directly — nothing issues one today, but
/// honouring it from the start means a future per-user grant is an Identity-only change;</item>
/// <item>any role the caller holds whose <see cref="RolePermissionBundles"/> entry contains it —
/// which is how every administrator is authorized today.</item>
/// </list>
///
/// <para>
/// <b>Registered as a singleton and it must stay stateless.</b> Ordering audit H1 was a singleton
/// authorization handler that captured a scoped <c>DbContext</c>; this one reads only the principal
/// and a static map, so it has nothing to capture. Resist adding a repository lookup here — an
/// authorization handler runs on every request to every protected endpoint.
/// </para>
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (HasDirectGrant(context.User, requirement.Permission)
            || HasRoleGrant(context.User, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    // Ordinal: a permission is an identifier, not prose. "Catalog.Write" is not "catalog.write",
    // and quietly treating them as the same would make the vocabulary's spelling unenforceable.
    private static bool HasDirectGrant(ClaimsPrincipal user, string permission)
        => user.HasClaim(EShopPermissions.ClaimType, permission);

    private static bool HasRoleGrant(ClaimsPrincipal user, string permission)
    {
        foreach (var role in user.FindAll(ClaimTypes.Role))
        {
            if (RolePermissionBundles.Grants(role.Value, permission))
            {
                return true;
            }
        }

        // A JWT may carry roles under the short "role" name rather than the long ClaimTypes.Role
        // URI, depending on whether the handler mapped inbound claims. Checking both is what keeps
        // this working regardless of MapInboundClaims.
        foreach (var role in user.FindAll("role"))
        {
            if (RolePermissionBundles.Grants(role.Value, permission))
            {
                return true;
            }
        }

        return false;
    }
}
