using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.Identity.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.DisableUserTwoFactor;

/// <summary>
/// Turns off a user's second factor (Admin panel S7, endpoint #14).
/// </summary>
/// <remarks>
/// <para>
/// The support path for a lost authenticator, and the reason it cannot simply reuse
/// <c>Disable2FACommand</c>: that one requires a valid TOTP code, which a user who has lost their
/// device by definition cannot produce. The code check there is load-bearing and must stay —
/// without it a stolen access token alone could strip the second factor
/// (<c>Disable2FACommandHandlerTests</c> pins the ordering) — so the admin path is a separate
/// command whose authorization is the check.
/// </para>
/// <para>
/// The authenticator key is reset as well as the flag cleared, mirroring the self-service handler:
/// re-enrolling must issue a fresh secret, or the old device would still work the moment 2FA is
/// turned back on.
/// </para>
/// </remarks>
public record DisableUserTwoFactorCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "User";

    string? IAuditedCommand.AuditEntityId => UserId;

    public string UserId { get; init; } = string.Empty;

    /// <summary><c>UserProfileResponse.TwoFactorEnabled</c> is cached.</summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class DisableUserTwoFactorCommandHandler : IRequestHandler<DisableUserTwoFactorCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DisableUserTwoFactorCommandHandler> _logger;

    public DisableUserTwoFactorCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<DisableUserTwoFactorCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DisableUserTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        // Refused rather than treated as a no-op, matching the self-service handler's answer: an
        // admin clicking this on an account without 2FA has misread the card, and silently
        // succeeding would confirm a state they did not create.
        if (!user.TwoFactorEnabled)
        {
            return Result<Unit>.Failure(AdminUserErrors.TwoFactorNotEnabled);
        }

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.SetTwoFactorEnabledAsync(user, false), "Disabling two-factor authentication");

        await _userManager.ResetAuthenticatorKeyAsync(user);

        _logger.LogInformation(
            "Admin disabled two-factor authentication and reset the authenticator key. UserId={UserId}",
            request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
