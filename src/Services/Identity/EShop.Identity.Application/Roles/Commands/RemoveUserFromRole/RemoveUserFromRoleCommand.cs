using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Roles.Commands.RemoveUserFromRole;

/// <summary>
/// Revokes a role. This is the security-critical direction of the pair: without invalidation the
/// removed role stays in the cached set for up to five minutes and every token minted in that
/// window still carries it.
///
/// Like its counterpart it does <b>not</b> use <c>ICacheInvalidatingCommand</c> for the roles cache — see
/// <c>AddUserToRoleCommand</c> for why that marker cannot reach this cache — and invalidates
/// through <c>ICachedUserRolesService</c> in the handler instead.
/// </summary>
public record RemoveUserFromRoleCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
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
