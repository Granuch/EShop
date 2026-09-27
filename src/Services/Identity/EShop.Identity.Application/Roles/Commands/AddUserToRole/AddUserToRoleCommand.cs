using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Roles.Commands.AddUserToRole;

/// <summary>
/// Grants a role.
///
/// <para>
/// <b>Deliberately NOT marked <c>ICacheInvalidatingCommand</c> for the roles cache, despite that
/// being the obvious move</b> (it is marked for the profile; see <see cref="CacheKeysToInvalidate"/>).
/// The plan for this stage called for exactly that, with key <c>user_roles:{UserId}</c>, so the
/// pipeline would replace the hand-written invalidation Stage 3 put in the controller. It does not work: <c>CacheInvalidationBehavior</c> rewrites
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
public record AddUserToRoleCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "User";

    string? IAuditedCommand.AuditEntityId => UserId;

    public string RoleName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// The cached profile, not the roles cache: <c>profile:{userId}</c> is written by
    /// <c>CachingBehavior</c>, so the marker can reach it, and it carries the user's roles and the
    /// permissions they grant (frontend-contracts F-07). <c>SetUserRolesCommand</c> declares the same
    /// key; without it a demoted admin's profile kept reporting every permission for five minutes.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}
