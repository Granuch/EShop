using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Roles.Commands.AddUserToRole;

/// <summary>
/// Grants a role.
///
/// <para>
/// <b>Deliberately NOT marked <c>ICacheInvalidatingCommand</c>, despite that being the obvious
/// move.</b> The plan for this stage called for exactly that, with key
/// <c>user_roles:{UserId}</c>, so the pipeline would replace the hand-written invalidation
/// Stage 3 put in the controller. It does not work: <c>CacheInvalidationBehavior</c> rewrites
/// every key as <c>{KeyPrefix}{Version}:{key}</c>, so it can only remove entries that
/// <c>CachingBehavior</c> itself wrote. <c>CachedUserRolesService</c> owns a separate,
/// unprefixed namespace, so the marker would have deleted a key nothing ever wrote — and
/// reported success while the stale role stayed cached for the full five minutes. The
/// SEC-02 regression test caught it; without that test this would have looked like a clean
/// structural improvement and silently reopened the vulnerability.
/// </para>
///
/// <para>
/// The handler therefore invalidates through <c>ICachedUserRolesService</c> directly. That is
/// still the structural win Stage 7 was after: the call moved out of the controller and into
/// the Application layer, where it runs inside the transaction and is unit-testable.
/// </para>
/// </summary>
public record AddUserToRoleCommand : IRequest<Result<Unit>>, ITransactionalCommand
{
    public string RoleName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
}
