using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.RestoreUser;

/// <summary>
/// Un-deletes an account (Admin panel S7, endpoint #9, decision Q1a).
/// </summary>
/// <remarks>
/// <para>
/// <b>The account comes back disabled.</b> <c>ApplicationUser.Restore()</c> clears the delete flags
/// and leaves <c>IsActive = false</c>, so bringing an account back is restore-then-activate: two
/// requests, two decisions. A one-click resurrection straight into a signed-in state is exactly
/// what <c>Activate()</c>'s refusal to touch deleted accounts was written to prevent.
/// </para>
/// <para>
/// <b>There is no SKU-conflict equivalent here, and that is worth stating because Catalog's
/// product restore has one (risk A3).</b> A product's unique SKU index is filtered on
/// <c>NOT "IsDeleted"</c>, so deleting a product frees its SKU and restoring can collide. ASP.NET
/// Identity's <c>UserNameIndex</c> is <b>not</b> filtered — a soft-deleted account keeps occupying
/// its address — so no other account can have taken it while it was gone. Adding a pre-check here
/// by analogy would be dead code; the property is pinned by a test instead.
/// </para>
/// </remarks>
public record RestoreUserCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class RestoreUserCommandHandler : IRequestHandler<RestoreUserCommand, Result<Unit>>
{
    private readonly IUserRepository _userRepository;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RestoreUserCommandHandler> _logger;

    public RestoreUserCommandHandler(
        IUserRepository userRepository,
        UserManager<ApplicationUser> userManager,
        ILogger<RestoreUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(RestoreUserCommand request, CancellationToken cancellationToken)
    {
        // The only command that lifts the soft-delete filter: GetByIdAsync would answer null
        // whatever the database holds, because the filter hides precisely the accounts this exists
        // to act on.
        var user = await _userRepository.GetByIdIncludingDeletedAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        // Checked before Restore() rather than relying on its idempotence, so the answer tells an
        // admin which of the two buttons they wanted. Mirrors Catalog's Product.NotDeleted.
        if (!user.IsDeleted)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotDeleted);
        }

        user.Restore();

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Restoring the account");

        _logger.LogInformation(
            "Admin restored a deleted account; it remains deactivated. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
