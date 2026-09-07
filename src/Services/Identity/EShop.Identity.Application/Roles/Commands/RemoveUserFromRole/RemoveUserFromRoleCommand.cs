using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Roles.Commands.RemoveUserFromRole;

/// <summary>
/// Revokes a role. This is the security-critical direction of the pair: without invalidation the
/// removed role stays in the cached set for up to five minutes and every token minted in that
/// window still carries it.
///
/// Like its counterpart it is deliberately <b>not</b> <c>ICacheInvalidatingCommand</c> — see
/// <c>AddUserToRoleCommand</c> for why that marker cannot reach this cache — and invalidates
/// through <c>ICachedUserRolesService</c> in the handler instead.
/// </summary>
public record RemoveUserFromRoleCommand : IRequest<Result<Unit>>, ITransactionalCommand
{
    public string RoleName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
}
