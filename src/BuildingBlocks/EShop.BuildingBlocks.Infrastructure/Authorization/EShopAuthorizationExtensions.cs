using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.Infrastructure.Authorization;

/// <summary>
/// Registers the permission model (decision Q4c) in a component.
///
/// <para>
/// Two calls, and both are needed: <see cref="AddEShopPermissions"/> registers the handler in DI,
/// and <see cref="AddEShopPermissionPolicies"/> registers one policy per permission inside the
/// component's existing <c>AddAuthorization</c> lambda. Registering the policies without the
/// handler is the silent-failure shape — every permission policy would then be unsatisfiable and
/// every admin endpoint would answer 403 — so <see cref="AddEShopPermissions"/> is what a
/// <c>Program.cs</c> should call, and it does both.
/// </para>
/// </summary>
public static class EShopAuthorizationExtensions
{
    /// <summary>
    /// Registers the permission handler and a policy per <see cref="EShopPermissions.All"/> entry.
    /// Safe to call alongside an existing <c>AddAuthorization</c> block: policies are additive and
    /// named for the permission, so nothing already registered is replaced.
    /// </summary>
    public static IServiceCollection AddEShopPermissions(this IServiceCollection services)
    {
        // Stateless, so a singleton — see PermissionAuthorizationHandler's remarks on Ordering H1.
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddAuthorization(options => options.AddEShopPermissionPolicies());

        return services;
    }

    /// <summary>
    /// Adds one policy per permission, named for the permission itself. Exposed separately so a
    /// component that already builds its <c>AddAuthorization</c> options in one place can keep
    /// doing so.
    /// </summary>
    public static AuthorizationOptions AddEShopPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in EShopPermissions.All)
        {
            options.AddPolicy(permission, policy => policy.AddRequirements(new PermissionRequirement(permission)));
        }

        return options;
    }
}
