using Microsoft.AspNetCore.Authorization;

namespace EShop.BuildingBlocks.Infrastructure.Authorization;

/// <summary>
/// Requires that the caller holds <b>at least one</b> permission from <see cref="EShopPermissions.All"/>, by a
/// <c>permission</c> claim or by a role bundle — the question "is this an operator at all?" (frontend-contracts F-08,
/// F-45).
///
/// <para>
/// <b>This is the gateway's admin gate, and it is deliberately coarse.</b> The gateway cannot know which permission
/// an endpoint needs without copying every service's policy table, and a copy would drift. So it asks only whether
/// the caller could be entitled to <i>anything</i> administrative, and the service still asks the exact question
/// (<c>catalog.write</c>, <c>payments.refund</c>, the <c>Admin</c> role, …). Both must pass. A customer holds no
/// permission and is refused at the gateway, as before; an operator whose role bundles only some permissions reaches
/// the service, which decides. <c>RequireRole("Admin")</c>, which this replaces at the gateway, refused such an
/// operator even where the service would have admitted them.
/// </para>
///
/// <para>
/// Like <see cref="PermissionRequirement"/> it carries no <c>RequireAuthenticatedUser()</c>: the authorization
/// middleware already answers 401 to an anonymous caller and 403 to a signed-in one that fails it.
/// </para>
/// </summary>
public sealed class AdminAreaRequirement : IAuthorizationRequirement
{
    /// <summary>The policy name, registered by <c>AddEShopPermissions()</c>.</summary>
    public const string PolicyName = "AdminArea";

    public override string ToString() => PolicyName;
}

/// <summary>
/// Grants <see cref="AdminAreaRequirement"/> when <see cref="PermissionAuthorizationHandler.Holds"/> is true for any
/// permission in the vocabulary. Stateless and registered as a singleton, for the same reason as
/// <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
public sealed class AdminAreaAuthorizationHandler : AuthorizationHandler<AdminAreaRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminAreaRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated == true
            && EShopPermissions.All.Any(permission => PermissionAuthorizationHandler.Holds(context.User, permission)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
