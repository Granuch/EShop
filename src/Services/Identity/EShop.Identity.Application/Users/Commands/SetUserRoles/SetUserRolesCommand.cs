using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.SetUserRoles;

/// <summary>
/// Replaces a user's whole role set in one request (Admin panel S7, endpoint #16).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <c>ICacheInvalidatingCommand</c> for the roles cache, and the reason is the highest-value
/// trap in this area.</b> <c>CacheInvalidationBehavior</c> rewrites every declared key as
/// <c>{KeyPrefix}{Version}:{key}</c>, so it can only remove what <c>CachingBehavior</c> wrote.
/// <c>CachedUserRolesService</c> owns an unprefixed <c>user_roles:{userId}</c> namespace, so
/// declaring that key would delete something nothing ever wrote, log success, and leave a demoted
/// user's Admin role in every token minted for the next five minutes — reopening SEC-02. The
/// handler calls <c>ICachedUserRolesService.InvalidateRolesCacheAsync</c> directly, exactly as
/// <c>AddUserToRoleCommandHandler</c> does.
/// </para>
/// <para>
/// It <i>is</i> marked for <c>profile:{userId}</c>, which is a different cache that
/// <c>CachingBehavior</c> genuinely wrote and whose response carries <c>Roles</c>. Two caches, two
/// mechanisms, and only one of them is reachable by the marker.
/// </para>
/// <para>
/// <b>Atomicity comes from checking every role before changing any of them.</b>
/// <c>TransactionBehavior</c> commits on a <c>Result</c> failure, so discovering an unknown role
/// halfway through the loop and returning a failure would leave the user with a half-applied set
/// while the caller is told nothing happened. That is what the plan's "partial failure leaves the
/// old set" means here, and it is a pre-check, not a rollback.
/// </para>
/// </remarks>
public record SetUserRolesCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "User";

    string? IAuditedCommand.AuditEntityId => UserId;

    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// The complete desired set. An empty array is legal and means "no roles" — this is a
    /// replacement, not a merge, so there has to be a way to express the empty set.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class SetUserRolesCommandValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User ID is required");

        // NotNull rather than NotEmpty: an empty list is the "strip every role" request. A missing
        // JSON member also arrives here as the record's [] initializer, which means the same thing.
        RuleFor(x => x.Roles)
            .NotNull().WithMessage("Roles are required")
            .Must(roles => roles.Count <= 10).WithMessage("A user may not be given more than 10 roles")
            .Must(roles => roles.All(r => !string.IsNullOrWhiteSpace(r)))
                .WithMessage("Role names must not be blank");
    }
}

public class SetUserRolesCommandHandler : IRequestHandler<SetUserRolesCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ICachedUserRolesService _cachedUserRoles;
    private readonly ILogger<SetUserRolesCommandHandler> _logger;

    public SetUserRolesCommandHandler(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ICachedUserRolesService cachedUserRoles,
        ILogger<SetUserRolesCommandHandler> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _cachedUserRoles = cachedUserRoles;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(SetUserRolesCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        var desired = request.Roles
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Every name validated before a single membership row moves. Moving this below the writes
        // is what turns "one bad name in the list" into a half-applied role set.
        foreach (var role in desired)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                _logger.LogWarning(
                    "Role replacement refused: unknown role. UserId={UserId}, Role={Role}",
                    request.UserId, role);
                return Result<Unit>.Failure(AdminUserErrors.RoleNotFound);
            }
        }

        var current = await _userManager.GetRolesAsync(user);

        // A delta rather than remove-all-then-add-all. Clearing first would drop and re-insert the
        // rows a user is keeping, which is pointless write amplification and — for the one role
        // that matters — a window inside the transaction where an Admin holds no Admin row.
        var toAdd = desired.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = current.Except(desired, StringComparer.OrdinalIgnoreCase).ToList();

        if (toRemove.Count > 0)
        {
            AdminUserErrors.EnsureSucceededAfterMutation(
                await _userManager.RemoveFromRolesAsync(user, toRemove), "Removing roles");
        }

        if (toAdd.Count > 0)
        {
            AdminUserErrors.EnsureSucceededAfterMutation(
                await _userManager.AddToRolesAsync(user, toAdd), "Adding roles");
        }

        if (toAdd.Count > 0 || toRemove.Count > 0)
        {
            // Tokens are minted from this cache, so without the eviction a demotion is invisible
            // for up to five minutes. Best-effort by design — InvalidateRolesCacheAsync catches and
            // logs rather than throwing — so a Redis outage still leaves the stale window open.
            await _cachedUserRoles.InvalidateRolesCacheAsync(request.UserId, cancellationToken);
        }

        _logger.LogInformation(
            "Admin replaced a user's roles. UserId={UserId}, Added={Added}, Removed={Removed}",
            request.UserId, string.Join(",", toAdd), string.Join(",", toRemove));

        return Result<Unit>.Success(Unit.Value);
    }
}
