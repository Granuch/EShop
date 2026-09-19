using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Identity.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.ActivateUser;

/// <summary>
/// Re-enables a deactivated account (Admin panel S7, endpoint #6).
/// </summary>
/// <remarks>
/// A deleted account cannot be reached here at all: <c>FindByIdAsync</c> runs under the
/// <c>!IsDeleted</c> global filter, so it answers not-found. That is the intended shape of decision
/// Q1a — bringing a deleted account back is <c>RestoreUserCommand</c> followed by this, two
/// separate decisions, and <c>ApplicationUser.Activate()</c> throwing on a deleted user is the
/// backstop for the same rule rather than a path this handler can take.
/// </remarks>
public record ActivateUserCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;

    /// <summary><c>UserProfileResponse</c> carries <c>IsActive</c>, so the cached profile is stale after this.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class ActivateUserCommandHandler : IRequestHandler<ActivateUserCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ActivateUserCommandHandler> _logger;

    public ActivateUserCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<ActivateUserCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        user.Activate();

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Activating the account");

        _logger.LogInformation("Admin activated an account. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
